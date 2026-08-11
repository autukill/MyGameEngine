namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.WorldStreaming;

public enum TileWorldChunkLoadMode
{
    Background = 0,
    Inline = 1
}

public readonly record struct TileWorldStreamingOptions
{
    public static TileWorldStreamingOptions Default => new(
        TileWorldLodSelectionOptions.Default,
        WorldChunkStreamingOptions.Default,
        TileWorldChunkLoadMode.Background);

    public TileWorldStreamingOptions(
        TileWorldLodSelectionOptions lodSelection,
        WorldChunkStreamingOptions chunkStreaming,
        TileWorldChunkLoadMode loadMode = TileWorldChunkLoadMode.Background)
    {
        if (!Enum.IsDefined(loadMode)) throw new ArgumentOutOfRangeException(nameof(loadMode));
        LodSelection = new TileWorldLodSelectionOptions(
            lodSelection.TargetPixelsPerTexel,
            lodSelection.HysteresisRatio);
        ChunkStreaming = new WorldChunkStreamingOptions(
            chunkStreaming.PreloadMarginChunks,
            chunkStreaming.RetainMarginChunks,
            chunkStreaming.MaximumConcurrentLoads,
            chunkStreaming.MaximumTrackedChunks,
            chunkStreaming.RetryFailedOnViewportChange,
            chunkStreaming.MaximumLoadsStartedPerUpdate);
        LoadMode = loadMode;
    }

    public TileWorldLodSelectionOptions LodSelection { get; }
    public WorldChunkStreamingOptions ChunkStreaming { get; }
    public TileWorldChunkLoadMode LoadMode { get; }
}

public readonly record struct TileWorldStreamingUpdateResult(
    int DesiredLevel,
    int ActiveLevel,
    int? PendingLevel,
    bool LevelChanged,
    int LoadsStarted,
    int LoadsCompleted,
    int ChunksUnloaded,
    int FailuresObserved);

public readonly record struct TileWorldStreamingDiagnostics(
    int DesiredLevel,
    int ActiveLevel,
    int FallbackLevel,
    int? PendingLevel,
    WorldChunkStreamingDiagnostics Fallback,
    WorldChunkStreamingDiagnostics Active,
    WorldChunkStreamingDiagnostics? Pending,
    bool HasFallbackSurfaces = false,
    bool FallbackSurfacesReady = false,
    int ResidentFallbackSurfaces = 0);

public readonly record struct TileWorldDrawStatistics(
    int RasterQuads,
    int TileSprites,
    int MissingActiveChunks,
    int FallbackQuads,
    int FallbackSurfaceQuads = 0);
