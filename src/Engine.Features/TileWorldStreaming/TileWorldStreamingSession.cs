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
    private readonly TileWorldBackgroundScheduler? _backgroundScheduler;
    private readonly string _scope;
    private readonly CancellationTokenSource _fallbackSurfaceCancellation = new();
    private readonly TileWorldLevelState _fallback;
    private readonly List<TileWorldLevelState> _retiredLevels = [];
    private Task<TileWorldFallbackSurfaceLease>? _fallbackSurfaceLoad;
    private TileWorldFallbackSurfaceLease? _fallbackSurface;
    private TileWorldLevelState _active;
    private TileWorldLevelState? _pending;
    private ViewportSnapshot _lastViewport;
    private int _desiredLevel;
    private bool _isUsingBudgetFallback;
    private long _requiredRetainedChunks;
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
            supplied.LoadMode,
            supplied.TextureUploadBudget);
        _selector = new TileWorldLodSelector(descriptor.Metadata, _options.LodSelection);
        if (_options.LoadMode == TileWorldChunkLoadMode.Background)
            _backgroundScheduler = new TileWorldBackgroundScheduler(
                _options.ChunkStreaming.MaximumConcurrentLoads);
        _scope = $"__tileworld-stream-{Interlocked.Increment(ref _nextScope)}-{descriptor.Ref.Name}";
        int fallbackLevel = descriptor.Metadata.DeclaredLodCount - 1;
        try
        {
            _fallback = CreateLevel(fallbackLevel);
        }
        catch
        {
            _backgroundScheduler?.Dispose();
            _fallbackSurfaceCancellation.Dispose();
            throw;
        }
        _active = _fallback;
        _desiredLevel = fallbackLevel;
        if (descriptor.Metadata.FallbackSurfaces.Count > 0)
        {
            try
            {
                var loader = new TileWorldFallbackSurfaceLoader(
                    descriptor,
                    _scope,
                    _decoder,
                    _options.LoadMode);
                _fallbackSurfaceLoad = loader.LoadAsync(_fallbackSurfaceCancellation.Token).AsTask();
            }
            catch
            {
                _fallback.Dispose();
                _backgroundScheduler?.Dispose();
                _fallbackSurfaceCancellation.Dispose();
                throw;
            }
        }
    }

    public TileWorldMetadata Metadata => _descriptor.Metadata;
    public int ActiveLevel => _active.Level;
    public int FallbackLevel => _fallback.Level;
    public int DesiredLevel => _desiredLevel;
    public int? PendingLevel => EffectivePendingLevel;
    public bool HasFallbackSurfaces => Metadata.FallbackSurfaces.Count > 0;
    public bool FallbackSurfacesReady => _fallbackSurface?.IsCommitted == true;
    public int ResidentFallbackSurfaceCount => _fallbackSurface?.Surfaces.Count ?? 0;

    public TileWorldStreamingUpdateResult Update(in ViewportSnapshot viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DrainRetiredLevels();
        var uploadBudget = new TileWorldTextureUploadBudgetState(_options.TextureUploadBudget);
        CompleteFallbackSurfaceLoad(ref uploadBudget);
        _desiredLevel = _selector.Select(viewport.Zoom);
        _lastViewport = viewport;
        _hasViewport = true;

        int started = 0;
        int completed = 0;
        int unloaded = 0;
        int failures = 0;
        bool levelChanged = false;

        _isUsingBudgetFallback = false;
        _requiredRetainedChunks = 0;

        bool fallbackTrackable = TryUpdateOrUseBudgetFallback(
            _fallback,
            viewport,
            ref uploadBudget,
            ref started,
            ref completed,
            ref unloaded,
            ref failures);
        // When zoom selects another LOD, keep the current level frozen as a visual bridge. Updating
        // it with the new Viewport can make a detailed level expand across the whole zoomed-out
        // world before the coarse candidate takes over, defeating both the residency cap and LOD.
        if (!ReferenceEquals(_active, _fallback) && _desiredLevel == _active.Level)
            TryUpdateOrUseBudgetFallback(
                _active,
                viewport,
                ref uploadBudget,
                ref started,
                ref completed,
                ref unloaded,
                ref failures);

        if (_desiredLevel == _active.Level)
        {
            RetirePending();
        }
        else if (_desiredLevel == _fallback.Level)
        {
            RetirePending();
            if ((fallbackTrackable && _fallback.IsVisibleReady(viewport)) ||
                (!fallbackTrackable && FallbackSurfacesReady))
            {
                TileWorldLevelState previous = _active;
                _active = _fallback;
                if (!ReferenceEquals(previous, _fallback)) RetireLevel(previous);
                levelChanged = true;
            }
        }
        else
        {
            if (_pending is null || _pending.Level != _desiredLevel)
            {
                RetirePending();
                _pending = CreateLevel(_desiredLevel);
            }
            bool pendingTrackable = TryUpdateOrUseBudgetFallback(
                _pending,
                viewport,
                ref uploadBudget,
                ref started,
                ref completed,
                ref unloaded,
                ref failures);
            if (pendingTrackable && _pending.IsVisibleReady(viewport))
            {
                TileWorldLevelState previous = _active;
                _active = _pending;
                _pending = null;
                if (!ReferenceEquals(previous, _fallback)) RetireLevel(previous);
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
            failures,
            uploadBudget.TexturesUploaded,
            uploadBudget.BytesUploaded,
            _retiredLevels.Count,
            _isUsingBudgetFallback,
            _requiredRetainedChunks);
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
        int fallbackSurfaceQuads = 0;

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
                ref fallbackQuads,
                ref fallbackSurfaceQuads);
        }

        return new TileWorldDrawStatistics(
            rasterQuads, tileSprites, missing, fallbackQuads, fallbackSurfaceQuads);
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
        int fallbackSurfaceQuads = 0;
        DrawLayerCore(
            batch,
            activeRange,
            layerIndex,
            color ?? Vector4.One,
            ref rasterQuads,
            ref tileSprites,
            ref fallbackQuads,
            ref fallbackSurfaceQuads);
        return new TileWorldDrawStatistics(
            rasterQuads,
            tileSprites,
            CountMissingActive(activeRange),
            fallbackQuads,
            fallbackSurfaceQuads);
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
            _pending?.Streamer.CaptureDiagnostics(),
            HasFallbackSurfaces,
            FallbackSurfacesReady,
            ResidentFallbackSurfaceCount,
            _retiredLevels.Count,
            _isUsingBudgetFallback,
            _requiredRetainedChunks);
    }

    public TileWorldStreamingMemoryDiagnostics CaptureMemoryDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int levelStates = 0;
        int residentLeases = 0;
        int inFlightLoads = 0;
        long preparedChunkBytes = 0;
        long authoritativeBytes = 0;
        long chunkGpuBytes = 0;

        AccumulateLevelMemory(
            _fallback,
            ref levelStates,
            ref residentLeases,
            ref inFlightLoads,
            ref preparedChunkBytes,
            ref authoritativeBytes,
            ref chunkGpuBytes);
        if (!ReferenceEquals(_active, _fallback))
            AccumulateLevelMemory(
                _active,
                ref levelStates,
                ref residentLeases,
                ref inFlightLoads,
                ref preparedChunkBytes,
                ref authoritativeBytes,
                ref chunkGpuBytes);
        if (_pending is not null)
            AccumulateLevelMemory(
                _pending,
                ref levelStates,
                ref residentLeases,
                ref inFlightLoads,
                ref preparedChunkBytes,
                ref authoritativeBytes,
                ref chunkGpuBytes);
        for (int index = 0; index < _retiredLevels.Count; index++)
            AccumulateLevelMemory(
                _retiredLevels[index],
                ref levelStates,
                ref residentLeases,
                ref inFlightLoads,
                ref preparedChunkBytes,
                ref authoritativeBytes,
                ref chunkGpuBytes);

        TileWorldFallbackSurfaceLease? fallbackSurface = _fallbackSurface;
        Task<TileWorldFallbackSurfaceLease>? fallbackTask = _fallbackSurfaceLoad;
        if (fallbackSurface is null && fallbackTask?.IsCompletedSuccessfully == true)
            fallbackSurface = fallbackTask.Result;
        bool fallbackInFlight = fallbackTask is not null && !fallbackTask.IsCompleted;
        return new TileWorldStreamingMemoryDiagnostics(
            levelStates,
            residentLeases,
            inFlightLoads,
            preparedChunkBytes,
            authoritativeBytes,
            chunkGpuBytes,
            fallbackInFlight,
            fallbackSurface?.PreparedDecodedBytes ?? 0,
            fallbackSurface?.EstimatedGpuTextureBytes ?? 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fallbackSurfaceCancellation.Cancel();
        Task<TileWorldFallbackSurfaceLease>? fallbackLoad = _fallbackSurfaceLoad;
        _fallbackSurfaceLoad = null;
        if (fallbackLoad is not null)
        {
            if (fallbackLoad.IsCompletedSuccessfully)
                fallbackLoad.Result.Dispose();
            else
                _ = fallbackLoad.ContinueWith(
                    static task =>
                    {
                        if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                        _ = task.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        _fallbackSurface?.Dispose();
        _fallbackSurface = null;
        _pending?.Dispose();
        _pending = null;
        for (int index = _retiredLevels.Count - 1; index >= 0; index--)
            _retiredLevels[index].Dispose();
        _retiredLevels.Clear();
        if (!ReferenceEquals(_active, _fallback)) _active.Dispose();
        _fallback.Dispose();
        _backgroundScheduler?.Dispose();
        _fallbackSurfaceCancellation.Dispose();
    }

    private TileWorldLevelState CreateLevel(int level) => new(
        _descriptor,
        level,
        $"{_scope}.state-{level}",
        _decoder,
        _options,
        _backgroundScheduler);

    private int? EffectivePendingLevel =>
        _pending?.Level ??
        (!ReferenceEquals(_active, _fallback) && _desiredLevel == _fallback.Level
            ? _fallback.Level
            : null);

    private static void AccumulateLevelMemory(
        TileWorldLevelState level,
        ref int levelStates,
        ref int residentLeases,
        ref int inFlightLoads,
        ref long preparedChunkBytes,
        ref long authoritativeBytes,
        ref long chunkGpuBytes)
    {
        TileWorldLevelMemoryDiagnostics memory = level.CaptureMemoryDiagnostics();
        levelStates++;
        residentLeases = checked(residentLeases + memory.ResidentChunkLeaseCount);
        inFlightLoads = checked(inFlightLoads + memory.InFlightChunkLoadCount);
        preparedChunkBytes = checked(preparedChunkBytes + memory.PreparedChunkDecodedBytes);
        authoritativeBytes = checked(authoritativeBytes + memory.AuthoritativeChunkPayloadBytes);
        chunkGpuBytes = checked(chunkGpuBytes + memory.EstimatedChunkGpuTextureBytes);
    }

    private bool TryUpdateOrUseBudgetFallback(
        TileWorldLevelState level,
        in ViewportSnapshot viewport,
        ref TileWorldTextureUploadBudgetState uploadBudget,
        ref int started,
        ref int completed,
        ref int unloaded,
        ref int failures)
    {
        long required = level.GetRequiredRetainedChunkCount(viewport);
        if (required <= _options.ChunkStreaming.MaximumTrackedChunks)
        {
            Accumulate(level.Update(viewport, _textures, ref uploadBudget),
                ref started, ref completed, ref unloaded, ref failures);
            return true;
        }

        if (!HasFallbackSurfaces)
        {
            // Preserve WorldChunkStreamer's explicit hard failure when no visual fallback exists.
            Accumulate(level.Update(viewport, _textures, ref uploadBudget),
                ref started, ref completed, ref unloaded, ref failures);
            return false;
        }

        Accumulate(level.Suspend(), ref started, ref completed, ref unloaded, ref failures);
        _isUsingBudgetFallback = true;
        _requiredRetainedChunks = Math.Max(_requiredRetainedChunks, required);
        return false;
    }

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
        ref int fallbackQuads,
        ref int fallbackSurfaceQuads)
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
                bool fallbackCovered = false;
                if (!covered && !ReferenceEquals(_active, _fallback))
                    fallbackCovered = DrawFallbackRegion(
                        batch,
                        coordinate,
                        layerIndex,
                        tint,
                        ref rasterQuads,
                        ref fallbackQuads);
                if (!covered && !fallbackCovered)
                    DrawFallbackSurfaceRegion(
                        batch,
                        coordinate,
                        layerIndex,
                        tint,
                        ref rasterQuads,
                        ref fallbackSurfaceQuads);
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

        if (lease.AuthoritativeData is not null)
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

    private bool DrawFallbackRegion(
        ISpriteBatch batch,
        WorldChunkCoordinate activeCoordinate,
        int layerIndex,
        Vector4 tint,
        ref int rasterQuads,
        ref int fallbackQuads)
    {
        int delta = _fallback.Level - _active.Level;
        if (delta <= 0) return false;
        int factor = 1 << delta;
        var fallbackCoordinate = new WorldChunkCoordinate(
            FloorDiv(activeCoordinate.X, factor),
            FloorDiv(activeCoordinate.Y, factor));
        if (!_fallback.TryGet(fallbackCoordinate, out TileWorldChunkLease? lease) ||
            lease is null || !lease.IsCommitted || !lease.HasPayload ||
            !lease.TryGetRasterLayer(layerIndex, out TileWorldRuntimeRasterLayer layer) ||
            !_textures.TryResolve(layer.Texture, out ResolvedTexture texture))
            return false;

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
        return true;
    }

    private void DrawFallbackSurfaceRegion(
        ISpriteBatch batch,
        WorldChunkCoordinate activeCoordinate,
        int layerIndex,
        Vector4 tint,
        ref int rasterQuads,
        ref int fallbackSurfaceQuads)
    {
        TileWorldFallbackSurfaceLease? lease = _fallbackSurface;
        if (lease is null || !lease.IsCommitted ||
            !lease.TryGet(layerIndex, out TileWorldRuntimeFallbackSurface surface) ||
            !_textures.TryResolve(surface.Texture, out ResolvedTexture texture))
            return;

        Bounds2D activeBounds = _active.Layout.GetBounds(activeCoordinate);
        Vector2 baseSize = Metadata.BaseChunkWorldSize;
        float worldLeft = Metadata.Bounds.MinX * baseSize.X;
        float worldTop = Metadata.Bounds.MinY * baseSize.Y;
        float worldRight = (Metadata.Bounds.MaxX + 1f) * baseSize.X;
        float worldBottom = (Metadata.Bounds.MaxY + 1f) * baseSize.Y;
        float left = MathF.Max(activeBounds.Left, worldLeft);
        float top = MathF.Max(activeBounds.Top, worldTop);
        float right = MathF.Min(activeBounds.Right, worldRight);
        float bottom = MathF.Min(activeBounds.Bottom, worldBottom);
        if (right <= left || bottom <= top) return;

        float inverseWidth = 1f / (worldRight - worldLeft);
        float inverseHeight = 1f / (worldBottom - worldTop);
        var uv = new Vector4(
            (left - worldLeft) * inverseWidth,
            (top - worldTop) * inverseHeight,
            (right - worldLeft) * inverseWidth,
            (bottom - worldTop) * inverseHeight);
        batch.Draw(
            texture.Handle,
            new Vector2(left, top),
            new Vector2(right - left, bottom - top),
            tint,
            uv);
        rasterQuads++;
        fallbackSurfaceQuads++;
    }

    private void CompleteFallbackSurfaceLoad(ref TileWorldTextureUploadBudgetState uploadBudget)
    {
        Task<TileWorldFallbackSurfaceLease>? task = _fallbackSurfaceLoad;
        if (_fallbackSurface is null && task is not null && task.IsCompleted)
        {
            _fallbackSurfaceLoad = null;
            _fallbackSurface = task.GetAwaiter().GetResult();
        }

        TileWorldFallbackSurfaceLease? lease = _fallbackSurface;
        if (lease is null || lease.IsCommitted) return;
        try
        {
            while (!lease.IsCommitted)
            {
                if (!lease.TryCommitNextTexture(_textures, ref uploadBudget)) return;
            }
        }
        catch
        {
            lease.Dispose();
            _fallbackSurface = null;
            throw;
        }
    }

    private void RetirePending()
    {
        if (_pending is not null) RetireLevel(_pending);
        _pending = null;
    }

    private void RetireLevel(TileWorldLevelState level)
    {
        level.BeginRetirement();
        _retiredLevels.Add(level);
    }

    private void DrainRetiredLevels()
    {
        for (int index = _retiredLevels.Count - 1; index >= 0; index--)
        {
            TileWorldLevelState level = _retiredLevels[index];
            if (!level.DrainRetirement()) continue;
            level.Dispose();
            _retiredLevels.RemoveAt(index);
        }
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
