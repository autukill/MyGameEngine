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
        TileWorldStreamingOptions options)
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
            options.LoadMode);
        Streamer = new WorldChunkStreamer<TileWorldChunkLease>(Layout, _loader, _options);
    }

    public int Level { get; }
    public WorldChunkLayout Layout { get; }
    public WorldChunkStreamer<TileWorldChunkLease> Streamer { get; }

    public WorldChunkUpdateResult Update(
        in ViewportSnapshot viewport,
        TextureLibrary textures)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WorldChunkUpdateResult result = Streamer.Update(viewport);
        CommitLoadedRange(viewport, textures);
        return result;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Streamer.Dispose();
        _loader.Dispose();
    }

    private void CommitLoadedRange(
        in ViewportSnapshot viewport,
        TextureLibrary textures)
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
                    lease is not null && !lease.IsCommitted)
                    lease.CommitTextures(textures);
                if (x == range.MaxX) break;
            }
            if (y == range.MaxY) break;
        }
    }
}
