namespace GameEngine.Features.WorldStreaming;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

public readonly record struct WorldChunkCoordinate(int X, int Y)
{
    public override string ToString() => $"({X}, {Y})";
}

public readonly record struct WorldChunkRange
{
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }
    public long Count => checked(
        checked((long)MaxX - MinX + 1L) *
        checked((long)MaxY - MinY + 1L));

    public WorldChunkRange(int minX, int minY, int maxX, int maxY)
    {
        if (minX > maxX) throw new ArgumentOutOfRangeException(nameof(minX));
        if (minY > maxY) throw new ArgumentOutOfRangeException(nameof(minY));
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public bool Contains(WorldChunkCoordinate coordinate) =>
        coordinate.X >= MinX && coordinate.X <= MaxX &&
        coordinate.Y >= MinY && coordinate.Y <= MaxY;

    public WorldChunkRange Expand(int chunks)
    {
        if (chunks < 0) throw new ArgumentOutOfRangeException(nameof(chunks));
        return new WorldChunkRange(
            checked(MinX - chunks),
            checked(MinY - chunks),
            checked(MaxX + chunks),
            checked(MaxY + chunks));
    }

    public WorldChunkRange Intersect(WorldChunkRange limits)
    {
        int minX = Math.Max(MinX, limits.MinX);
        int minY = Math.Max(MinY, limits.MinY);
        int maxX = Math.Min(MaxX, limits.MaxX);
        int maxY = Math.Min(MaxY, limits.MaxY);
        if (minX > maxX || minY > maxY)
            throw new InvalidOperationException("World bounds do not intersect the configured chunk limits.");
        return new WorldChunkRange(minX, minY, maxX, maxY);
    }
}

public readonly record struct WorldChunkLayout
{
    public Vector2 Origin { get; }
    public Vector2 ChunkSize { get; }
    public WorldChunkRange? Limits { get; }

    public WorldChunkLayout(Vector2 chunkSize, Vector2? origin = null, WorldChunkRange? limits = null)
    {
        Vector2 resolvedOrigin = origin ?? Vector2.Zero;
        if (!float.IsFinite(chunkSize.X) || !float.IsFinite(chunkSize.Y) ||
            chunkSize.X <= 0f || chunkSize.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        if (!float.IsFinite(resolvedOrigin.X) || !float.IsFinite(resolvedOrigin.Y))
            throw new ArgumentOutOfRangeException(nameof(origin));
        Origin = resolvedOrigin;
        ChunkSize = chunkSize;
        Limits = limits;
    }

    public WorldChunkRange GetRange(Bounds2D worldBounds, int expansionChunks = 0)
    {
        if (worldBounds.Width <= 0f || worldBounds.Height <= 0f)
            throw new ArgumentException("World bounds must have positive area.", nameof(worldBounds));
        if (expansionChunks < 0) throw new ArgumentOutOfRangeException(nameof(expansionChunks));
        int minX = FloorToChunk(worldBounds.Left, Origin.X, ChunkSize.X);
        int minY = FloorToChunk(worldBounds.Top, Origin.Y, ChunkSize.Y);
        int maxX = FloorToChunk(MathF.BitDecrement(worldBounds.Right), Origin.X, ChunkSize.X);
        int maxY = FloorToChunk(MathF.BitDecrement(worldBounds.Bottom), Origin.Y, ChunkSize.Y);
        WorldChunkRange range = new WorldChunkRange(minX, minY, maxX, maxY).Expand(expansionChunks);
        return Limits is { } limits ? range.Intersect(limits) : range;
    }

    public Bounds2D GetBounds(WorldChunkCoordinate coordinate)
    {
        float left = Origin.X + coordinate.X * ChunkSize.X;
        float top = Origin.Y + coordinate.Y * ChunkSize.Y;
        return new Bounds2D(left, top, left + ChunkSize.X, top + ChunkSize.Y);
    }

    private static int FloorToChunk(float value, float origin, float size)
    {
        float coordinate = MathF.Floor((value - origin) / size);
        if (!float.IsFinite(coordinate) || coordinate < int.MinValue || coordinate > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        return (int)coordinate;
    }
}

public enum WorldChunkResidency
{
    None = 0,
    Retained = 1,
    Preloaded = 2,
    Visible = 3,
}

public enum WorldChunkLoadState
{
    Pending = 0,
    Loading = 1,
    Loaded = 2,
    Failed = 3,
}

public readonly record struct WorldChunkStreamingOptions
{
    public static WorldChunkStreamingOptions Default => new(1, 2, 4, 4_096, true, 8);

    public int PreloadMarginChunks { get; }
    public int RetainMarginChunks { get; }
    public int MaximumConcurrentLoads { get; }
    public int MaximumTrackedChunks { get; }
    public bool RetryFailedOnViewportChange { get; }
    public int MaximumLoadsStartedPerUpdate { get; }

    public WorldChunkStreamingOptions() : this(1, 2, 4, 4_096, true, 8) { }

    public WorldChunkStreamingOptions(
        int preloadMarginChunks = 1,
        int retainMarginChunks = 2,
        int maximumConcurrentLoads = 4,
        int maximumTrackedChunks = 4_096,
        bool retryFailedOnViewportChange = true,
        int maximumLoadsStartedPerUpdate = 8)
    {
        if (preloadMarginChunks < 0) throw new ArgumentOutOfRangeException(nameof(preloadMarginChunks));
        if (retainMarginChunks < preloadMarginChunks)
            throw new ArgumentOutOfRangeException(nameof(retainMarginChunks));
        if (maximumConcurrentLoads <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentLoads));
        if (maximumTrackedChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTrackedChunks));
        if (maximumLoadsStartedPerUpdate <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLoadsStartedPerUpdate));
        PreloadMarginChunks = preloadMarginChunks;
        RetainMarginChunks = retainMarginChunks;
        MaximumConcurrentLoads = maximumConcurrentLoads;
        MaximumTrackedChunks = maximumTrackedChunks;
        RetryFailedOnViewportChange = retryFailedOnViewportChange;
        MaximumLoadsStartedPerUpdate = maximumLoadsStartedPerUpdate;
    }
}

public interface IWorldChunkLoader<TChunk> where TChunk : class, IDisposable
{
    ValueTask<TChunk> LoadAsync(WorldChunkCoordinate coordinate, CancellationToken cancellationToken);
}

public readonly record struct WorldChunkLoadedEvent(WorldChunkCoordinate Coordinate);
public readonly record struct WorldChunkUnloadedEvent(WorldChunkCoordinate Coordinate);
public readonly record struct WorldChunkFailedEvent(
    WorldChunkCoordinate Coordinate,
    Exception Exception,
    ulong ViewportRevision);

public readonly record struct WorldChunkStreamingDiagnostics(
    int TrackedCount,
    int PendingCount,
    int LoadingCount,
    int LoadedCount,
    int FailedCount,
    int VisibleCount,
    int PreloadedCount,
    int RetainedCount,
    ulong LastViewportRevision);

public readonly record struct WorldChunkUpdateResult(
    bool DesiredSetChanged,
    int LoadsStarted,
    int LoadsCompleted,
    int ChunksUnloaded,
    int FailuresObserved);
