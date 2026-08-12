namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.WorldStreaming;

public enum TileWorldChunkLoadMode
{
    Background = 0,
    Inline = 1
}

/// <summary>
/// Limits graphics-thread RGBA uploads per Session update. The first Texture may exceed the byte
/// limit so an oversized but otherwise valid offline Chunk cannot starve forever.
/// </summary>
public readonly record struct TileWorldTextureUploadBudget
{
    public static TileWorldTextureUploadBudget Default => new(
        maximumTexturesPerUpdate: 2,
        maximumBytesPerUpdate: 2 * 1_024 * 1_024);

    public TileWorldTextureUploadBudget(
        int maximumTexturesPerUpdate,
        long maximumBytesPerUpdate)
    {
        if (maximumTexturesPerUpdate <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTexturesPerUpdate));
        if (maximumBytesPerUpdate <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytesPerUpdate));
        MaximumTexturesPerUpdate = maximumTexturesPerUpdate;
        MaximumBytesPerUpdate = maximumBytesPerUpdate;
    }

    public int MaximumTexturesPerUpdate { get; }
    public long MaximumBytesPerUpdate { get; }
}

/// <summary>
/// Caps the conservative RGBA8 cost of Raster Chunk textures in one retained level. Preview
/// surfaces and renderer-owned targets are outside this budget and remain independently visible.
/// </summary>
public readonly record struct TileWorldTextureResidencyBudget
{
    public static TileWorldTextureResidencyBudget Unlimited => new(long.MaxValue);

    public TileWorldTextureResidencyBudget(long maximumChunkTextureBytes)
    {
        if (maximumChunkTextureBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunkTextureBytes));
        MaximumChunkTextureBytes = maximumChunkTextureBytes;
    }

    public long MaximumChunkTextureBytes { get; }
}

[Flags]
public enum TileWorldBudgetFallbackReason
{
    None = 0,
    MaximumTrackedChunks = 1,
    MaximumChunkTextureBytes = 2
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
        TileWorldChunkLoadMode loadMode = TileWorldChunkLoadMode.Background,
        TileWorldTextureUploadBudget? textureUploadBudget = null,
        TileWorldTextureResidencyBudget? textureResidencyBudget = null)
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
        TileWorldTextureUploadBudget upload =
            textureUploadBudget ?? TileWorldTextureUploadBudget.Default;
        TextureUploadBudget = new TileWorldTextureUploadBudget(
            upload.MaximumTexturesPerUpdate,
            upload.MaximumBytesPerUpdate);
        TileWorldTextureResidencyBudget residency =
            textureResidencyBudget ?? TileWorldTextureResidencyBudget.Unlimited;
        TextureResidencyBudget = new TileWorldTextureResidencyBudget(
            residency.MaximumChunkTextureBytes);
    }

    public TileWorldLodSelectionOptions LodSelection { get; }
    public WorldChunkStreamingOptions ChunkStreaming { get; }
    public TileWorldChunkLoadMode LoadMode { get; }
    public TileWorldTextureUploadBudget TextureUploadBudget { get; }
    public TileWorldTextureResidencyBudget TextureResidencyBudget { get; }
}

public readonly record struct TileWorldStreamingUpdateResult(
    int DesiredLevel,
    int ActiveLevel,
    int? PendingLevel,
    bool LevelChanged,
    int LoadsStarted,
    int LoadsCompleted,
    int ChunksUnloaded,
    int FailuresObserved,
    int TexturesUploaded = 0,
    long TextureBytesUploaded = 0,
    int RetiringLevels = 0,
    bool IsUsingBudgetFallback = false,
    long RequiredRetainedChunks = 0,
    TileWorldBudgetFallbackReason BudgetFallbackReason = TileWorldBudgetFallbackReason.None,
    long RequiredRetainedTextureBytes = 0);

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
    int ResidentFallbackSurfaces = 0,
    int RetiringLevels = 0,
    bool IsUsingBudgetFallback = false,
    long RequiredRetainedChunks = 0,
    TileWorldBudgetFallbackReason BudgetFallbackReason = TileWorldBudgetFallbackReason.None,
    long RequiredRetainedTextureBytes = 0);

public readonly record struct TileWorldDrawStatistics(
    int RasterQuads,
    int TileSprites,
    int MissingActiveChunks,
    int FallbackQuads,
    int FallbackSurfaceQuads = 0);

/// <summary>
/// Low-frequency ownership snapshot. CPU values count payload arrays still referenced by
/// TileWorld leases; GPU values are logical RGBA8 estimates and may overlap Hosting Texture totals.
/// </summary>
public readonly record struct TileWorldStreamingMemoryDiagnostics(
    int LevelStateCount,
    int ResidentChunkLeaseCount,
    int InFlightChunkLoadCount,
    long PreparedChunkDecodedBytes,
    long AuthoritativeChunkPayloadBytes,
    long EstimatedChunkGpuTextureBytes,
    bool IsFallbackSurfaceLoadInFlight,
    long PreparedFallbackDecodedBytes,
    long EstimatedFallbackGpuTextureBytes)
{
    public long OwnedCpuPayloadBytes => checked(
        PreparedChunkDecodedBytes +
        AuthoritativeChunkPayloadBytes +
        PreparedFallbackDecodedBytes);

    public long EstimatedGpuTextureBytes => checked(
        EstimatedChunkGpuTextureBytes + EstimatedFallbackGpuTextureBytes);
}
