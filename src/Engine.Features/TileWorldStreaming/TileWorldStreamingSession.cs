namespace GameEngine.Features.TileWorldStreaming;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;

/// <summary>
/// Connects Viewport-driven LOD selection to TileWorld archive streaming. The coarsest generated
/// level remains resident as a visual fallback; a candidate replaces the active level only after
/// every currently visible Chunk has loaded and committed its textures.
/// </summary>
public sealed class TileWorldStreamingSession : IDisposable
{
    private static long _nextScope;

    private readonly TileWorldDescriptor _descriptor;
    private readonly TileSetLibrary _tileSets;
    private readonly TextureLibrary _textures;
    private readonly IImageDecoder _decoder;
    private readonly TileWorldStreamingOptions _options;
    private readonly TileWorldLodSelector _selector;
    private readonly string _scope;
    private readonly TileWorldLevelState _fallback;
    private TileWorldLevelState _active;
    private TileWorldLevelState? _pending;
    private ViewportSnapshot _lastViewport;
    private int _desiredLevel;
    private bool _hasViewport;
    private bool _disposed;

    public TileWorldStreamingSession(
        TileWorldDescriptor descriptor,
        TileSetLibrary tileSets,
        TextureLibrary textures,
        IImageDecoder? decoder = null,
        TileWorldStreamingOptions? options = null)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _tileSets = tileSets ?? throw new ArgumentNullException(nameof(tileSets));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _decoder = decoder ?? new SkiaImageDecoder();
        TileWorldStreamingOptions supplied = options ?? TileWorldStreamingOptions.Default;
        _options = new TileWorldStreamingOptions(
            supplied.LodSelection,
            supplied.ChunkStreaming,
            supplied.LoadMode);
        _selector = new TileWorldLodSelector(descriptor.Metadata, _options.LodSelection);
        _scope = $"__tileworld-stream-{Interlocked.Increment(ref _nextScope)}-{descriptor.Ref.Name}";
        int fallbackLevel = descriptor.Metadata.DeclaredLodCount - 1;
        _fallback = CreateLevel(fallbackLevel);
        _active = _fallback;
        _desiredLevel = fallbackLevel;
    }

    public TileWorldMetadata Metadata => _descriptor.Metadata;
    public int ActiveLevel => _active.Level;
    public int FallbackLevel => _fallback.Level;
    public int DesiredLevel => _desiredLevel;
    public int? PendingLevel => EffectivePendingLevel;

    public TileWorldStreamingUpdateResult Update(in ViewportSnapshot viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _desiredLevel = _selector.Select(viewport.Zoom);
        _lastViewport = viewport;
        _hasViewport = true;

        int started = 0;
        int completed = 0;
        int unloaded = 0;
        int failures = 0;
        bool levelChanged = false;

        Accumulate(_fallback.Update(viewport, _textures),
            ref started, ref completed, ref unloaded, ref failures);
        if (!ReferenceEquals(_active, _fallback))
            Accumulate(_active.Update(viewport, _textures),
                ref started, ref completed, ref unloaded, ref failures);

        if (_desiredLevel == _active.Level)
        {
            DisposePending();
        }
        else if (_desiredLevel == _fallback.Level)
        {
            DisposePending();
            if (_fallback.IsVisibleReady(viewport))
            {
                TileWorldLevelState previous = _active;
                _active = _fallback;
                if (!ReferenceEquals(previous, _fallback)) previous.Dispose();
                levelChanged = true;
            }
        }
        else
        {
            if (_pending is null || _pending.Level != _desiredLevel)
            {
                DisposePending();
                _pending = CreateLevel(_desiredLevel);
            }
            Accumulate(_pending.Update(viewport, _textures),
                ref started, ref completed, ref unloaded, ref failures);
            if (_pending.IsVisibleReady(viewport))
            {
                TileWorldLevelState previous = _active;
                _active = _pending;
                _pending = null;
                if (!ReferenceEquals(previous, _fallback)) previous.Dispose();
                levelChanged = true;
            }
        }

        return new TileWorldStreamingUpdateResult(
            _desiredLevel,
            _active.Level,
            EffectivePendingLevel,
            levelChanged,
            started,
            completed,
            unloaded,
            failures);
    }

    public TileWorldDrawStatistics Draw(
        ISpriteBatch batch,
        Vector4? color = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        if (!_hasViewport)
            throw new InvalidOperationException("Call Update before drawing a TileWorld stream.");

        Vector4 tint = color ?? Vector4.One;
        WorldChunkRange activeRange = _active.Layout.GetRange(_lastViewport.VisibleWorldBounds);
        int missing = CountMissingActive(activeRange);
        int rasterQuads = 0;
        int tileSprites = 0;
        int fallbackQuads = 0;

        for (int layerIndex = 0; layerIndex < Metadata.Layers.Count; layerIndex++)
        {
            if (!Metadata.Layers[layerIndex].Visible) continue;
            DrawLayerCore(
                batch,
                activeRange,
                layerIndex,
                tint,
                ref rasterQuads,
                ref tileSprites,
                ref fallbackQuads);
        }

        return new TileWorldDrawStatistics(
            rasterQuads, tileSprites, missing, fallbackQuads);
    }

    /// <summary>
    /// Draws one metadata Layer so gameplay objects can be interleaved between TileWorld depths.
    /// </summary>
    public TileWorldDrawStatistics DrawLayer(
        ISpriteBatch batch,
        int layerIndex,
        Vector4? color = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        if (!_hasViewport)
            throw new InvalidOperationException("Call Update before drawing a TileWorld stream.");
        if ((uint)layerIndex >= (uint)Metadata.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (!Metadata.Layers[layerIndex].Visible) return default;

        WorldChunkRange activeRange = _active.Layout.GetRange(_lastViewport.VisibleWorldBounds);
        int rasterQuads = 0;
        int tileSprites = 0;
        int fallbackQuads = 0;
        DrawLayerCore(
            batch,
            activeRange,
            layerIndex,
            color ?? Vector4.One,
            ref rasterQuads,
            ref tileSprites,
            ref fallbackQuads);
        return new TileWorldDrawStatistics(
            rasterQuads,
            tileSprites,
            CountMissingActive(activeRange),
            fallbackQuads);
    }

    public bool TryGetActiveChunk(
        WorldChunkCoordinate coordinate,
        out TileWorldChunkLease? lease)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _active.TryGet(coordinate, out lease);
    }

    public TileWorldStreamingDiagnostics CaptureDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new TileWorldStreamingDiagnostics(
            _desiredLevel,
            _active.Level,
            _fallback.Level,
            EffectivePendingLevel,
            _fallback.Streamer.CaptureDiagnostics(),
            _active.Streamer.CaptureDiagnostics(),
            _pending?.Streamer.CaptureDiagnostics());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposePending();
        if (!ReferenceEquals(_active, _fallback)) _active.Dispose();
        _fallback.Dispose();
    }

    private TileWorldLevelState CreateLevel(int level) => new(
        _descriptor,
        level,
        $"{_scope}.state-{level}",
        _decoder,
        _options);

    private int? EffectivePendingLevel =>
        _pending?.Level ??
        (!ReferenceEquals(_active, _fallback) && _desiredLevel == _fallback.Level
            ? _fallback.Level
            : null);

    private int CountMissingActive(WorldChunkRange range)
    {
        int missing = 0;
        for (int y = range.MinY; ; y++)
        {
            for (int x = range.MinX; ; x++)
            {
                if (!_active.TryGet(new WorldChunkCoordinate(x, y), out TileWorldChunkLease? lease) ||
                    lease is null || !lease.IsCommitted)
                    missing++;
                if (x == range.MaxX) break;
            }
            if (y == range.MaxY) break;
        }
        return missing;
    }

    private void DrawLayerCore(
        ISpriteBatch batch,
        WorldChunkRange activeRange,
        int layerIndex,
        Vector4 tint,
        ref int rasterQuads,
        ref int tileSprites,
        ref int fallbackQuads)
    {
        for (int y = activeRange.MinY; ; y++)
        {
            for (int x = activeRange.MinX; ; x++)
            {
                var coordinate = new WorldChunkCoordinate(x, y);
                bool covered = TryDrawActiveLayer(
                    batch,
                    coordinate,
                    layerIndex,
                    tint,
                    ref rasterQuads,
                    ref tileSprites);
                if (!covered && !ReferenceEquals(_active, _fallback))
                    DrawFallbackRegion(
                        batch,
                        coordinate,
                        layerIndex,
                        tint,
                        ref rasterQuads,
                        ref fallbackQuads);
                if (x == activeRange.MaxX) break;
            }
            if (y == activeRange.MaxY) break;
        }
    }

    private bool TryDrawActiveLayer(
        ISpriteBatch batch,
        WorldChunkCoordinate coordinate,
        int layerIndex,
        Vector4 tint,
        ref int rasterQuads,
        ref int tileSprites)
    {
        if (!_active.TryGet(coordinate, out TileWorldChunkLease? lease) ||
            lease is null || !lease.IsCommitted)
            return false;
        if (!lease.HasPayload) return true;

        if (_active.Level == 0)
            return DrawAuthoritativeLayer(
                batch, lease, coordinate, layerIndex, tint, ref tileSprites);

        if (!lease.TryGetRasterLayer(layerIndex, out TileWorldRuntimeRasterLayer layer))
            return true;
        if (!_textures.TryResolve(layer.Texture, out ResolvedTexture texture)) return false;
        Bounds2D bounds = _active.Layout.GetBounds(coordinate);
        batch.Draw(
            texture.Handle,
            new Vector2(bounds.Left, bounds.Top),
            new Vector2(bounds.Width, bounds.Height),
            tint,
            layer.InnerUvBounds);
        rasterQuads++;
        return true;
    }

    private bool DrawAuthoritativeLayer(
        ISpriteBatch batch,
        TileWorldChunkLease lease,
        WorldChunkCoordinate coordinate,
        int layerIndex,
        Vector4 tint,
        ref int tileSprites)
    {
        TileWorldChunkData? chunk = lease.AuthoritativeData;
        if (chunk is null) return true;
        TileWorldChunkLayerData? chunkLayer = null;
        for (int index = 0; index < chunk.Layers.Count; index++)
        {
            if (chunk.Layers[index].LayerIndex != layerIndex) continue;
            chunkLayer = chunk.Layers[index];
            break;
        }
        if (chunkLayer is null) return true;
        TileWorldLayerMetadata layer = Metadata.Layers[layerIndex];
        if (!_tileSets.TryGet(layer.TileSet, out TileSet tileSet)) return false;

        Vector2 tileSize = Metadata.TileSize;
        long chunkCellX = (long)coordinate.X * Metadata.ChunkWidth;
        long chunkCellY = (long)coordinate.Y * Metadata.ChunkHeight;
        for (int localY = 0; localY < Metadata.ChunkHeight; localY++)
        {
            for (int localX = 0; localX < Metadata.ChunkWidth; localX++)
            {
                TileCell cell = chunkLayer.Cells[localY * Metadata.ChunkWidth + localX];
                if (cell.IsEmpty || !tileSet.TryGet(cell.Tile, out TileDefinition definition)) continue;
                Vector2 center = layer.Offset + new Vector2(
                    ((float)chunkCellX + localX + 0.5f) * tileSize.X,
                    ((float)chunkCellY + localY + 0.5f) * tileSize.Y);
                TileTransformOperations.GetScaleAndRotation(
                    cell.Transform, out Vector2 scale, out float rotation);
                batch.DrawSpriteCommand(new SpriteDrawCommand(
                    definition.Sprite,
                    definition.SubImage,
                    center,
                    scale,
                    rotation,
                    tint,
                    SizeOverride: tileSize,
                    OriginOverride: tileSize * 0.5f));
                tileSprites++;
            }
        }
        return true;
    }

    private void DrawFallbackRegion(
        ISpriteBatch batch,
        WorldChunkCoordinate activeCoordinate,
        int layerIndex,
        Vector4 tint,
        ref int rasterQuads,
        ref int fallbackQuads)
    {
        int delta = _fallback.Level - _active.Level;
        if (delta <= 0) return;
        int factor = 1 << delta;
        var fallbackCoordinate = new WorldChunkCoordinate(
            FloorDiv(activeCoordinate.X, factor),
            FloorDiv(activeCoordinate.Y, factor));
        if (!_fallback.TryGet(fallbackCoordinate, out TileWorldChunkLease? lease) ||
            lease is null || !lease.IsCommitted || !lease.HasPayload ||
            !lease.TryGetRasterLayer(layerIndex, out TileWorldRuntimeRasterLayer layer) ||
            !_textures.TryResolve(layer.Texture, out ResolvedTexture texture))
            return;

        Bounds2D activeBounds = _active.Layout.GetBounds(activeCoordinate);
        Bounds2D fallbackBounds = _fallback.Layout.GetBounds(fallbackCoordinate);
        float relativeLeft = Math.Clamp(
            (activeBounds.Left - fallbackBounds.Left) / fallbackBounds.Width, 0f, 1f);
        float relativeTop = Math.Clamp(
            (activeBounds.Top - fallbackBounds.Top) / fallbackBounds.Height, 0f, 1f);
        float relativeRight = Math.Clamp(
            (activeBounds.Right - fallbackBounds.Left) / fallbackBounds.Width, 0f, 1f);
        float relativeBottom = Math.Clamp(
            (activeBounds.Bottom - fallbackBounds.Top) / fallbackBounds.Height, 0f, 1f);
        Vector4 inner = layer.InnerUvBounds;
        var uv = new Vector4(
            Lerp(inner.X, inner.Z, relativeLeft),
            Lerp(inner.Y, inner.W, relativeTop),
            Lerp(inner.X, inner.Z, relativeRight),
            Lerp(inner.Y, inner.W, relativeBottom));
        batch.Draw(
            texture.Handle,
            new Vector2(activeBounds.Left, activeBounds.Top),
            new Vector2(activeBounds.Width, activeBounds.Height),
            tint,
            uv);
        rasterQuads++;
        fallbackQuads++;
    }

    private void DisposePending()
    {
        _pending?.Dispose();
        _pending = null;
    }

    private static void Accumulate(
        WorldChunkUpdateResult result,
        ref int started,
        ref int completed,
        ref int unloaded,
        ref int failures)
    {
        started += result.LoadsStarted;
        completed += result.LoadsCompleted;
        unloaded += result.ChunksUnloaded;
        failures += result.FailuresObserved;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    private static float Lerp(float start, float end, float amount) =>
        start + (end - start) * amount;
}
