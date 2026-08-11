namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;

internal sealed class TileWorldLevelState : IDisposable
{
    private readonly TileWorldChunkLoader _loader;
    private readonly WorldChunkStreamingOptions _options;
    private bool _disposed;

    public TileWorldLevelState(
        TileWorldDescriptor descriptor,
        int level,
        string textureScope,
        IImageDecoder decoder,
        TileWorldStreamingOptions options,
        TileWorldBackgroundScheduler? backgroundScheduler)
    {
        Level = level;
        _options = options.ChunkStreaming;
        TileWorldMetadata metadata = descriptor.Metadata;
        int factor = 1 << level;
        TileWorldChunkBounds bounds = metadata.GetChunkBounds(level);
        Layout = new WorldChunkLayout(
            metadata.BaseChunkWorldSize * factor,
            limits: new WorldChunkRange(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY));
        _loader = new TileWorldChunkLoader(
            descriptor,
            level,
            textureScope,
            decoder,
            options.LoadMode,
            backgroundScheduler);
        Streamer = new WorldChunkStreamer<TileWorldChunkLease>(Layout, _loader, _options);
    }

    public int Level { get; }
    public WorldChunkLayout Layout { get; }
    public WorldChunkStreamer<TileWorldChunkLease> Streamer { get; }

    public long GetRequiredRetainedChunkCount(in ViewportSnapshot viewport) =>
        Layout.GetRange(viewport.VisibleWorldBounds, _options.RetainMarginChunks).Count;

    public bool CanTrack(in ViewportSnapshot viewport) =>
        GetRequiredRetainedChunkCount(viewport) <= _options.MaximumTrackedChunks;

    public WorldChunkUpdateResult Update(
        in ViewportSnapshot viewport,
        TextureLibrary textures,
        ref TileWorldTextureUploadBudgetState uploadBudget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WorldChunkUpdateResult result = Streamer.Update(viewport);
        CommitLoadedRange(viewport, textures, ref uploadBudget);
        return result;
    }

    public WorldChunkUpdateResult Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Streamer.Suspend();
    }

    public bool IsVisibleReady(in ViewportSnapshot viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WorldChunkRange range = Layout.GetRange(viewport.VisibleWorldBounds);
        for (int y = range.MinY; ; y++)
        {
            for (int x = range.MinX; ; x++)
            {
                if (!Streamer.TryGetChunk(new WorldChunkCoordinate(x, y), out TileWorldChunkLease? lease) ||
                    lease is null || !lease.IsCommitted)
                    return false;
                if (x == range.MaxX) break;
            }
            if (y == range.MaxY) break;
        }
        return true;
    }

    public bool TryGet(
        WorldChunkCoordinate coordinate,
        out TileWorldChunkLease? lease) => Streamer.TryGetChunk(coordinate, out lease);

    public void BeginRetirement()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Streamer.BeginRetirement();
    }

    public bool DrainRetirement()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Streamer.DrainRetirement();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Streamer.Dispose();
        _loader.Dispose();
    }

    private void CommitLoadedRange(
        in ViewportSnapshot viewport,
        TextureLibrary textures,
        ref TileWorldTextureUploadBudgetState uploadBudget)
    {
        WorldChunkRange range = Layout.GetRange(
            viewport.VisibleWorldBounds,
            _options.PreloadMarginChunks);
        for (int y = range.MinY; ; y++)
        {
            for (int x = range.MinX; ; x++)
            {
                if (Streamer.TryGetChunk(
                        new WorldChunkCoordinate(x, y),
                        out TileWorldChunkLease? lease) &&
                    lease is not null)
                {
                    while (!lease.IsCommitted)
                    {
                        if (!lease.TryCommitNextTexture(textures, ref uploadBudget)) return;
                    }
                }
                if (x == range.MaxX) break;
            }
            if (y == range.MaxY) break;
        }
    }
}
