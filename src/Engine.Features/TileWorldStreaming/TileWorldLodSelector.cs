namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.TileWorlds.Domain;

public readonly record struct TileWorldLodSelectionOptions
{
    public static TileWorldLodSelectionOptions Default => new(1f, 0.1f);

    public TileWorldLodSelectionOptions(
        float targetPixelsPerTexel = 1f,
        float hysteresisRatio = 0.1f)
    {
        if (!float.IsFinite(targetPixelsPerTexel) ||
            targetPixelsPerTexel is <= 0f or > 8f)
            throw new ArgumentOutOfRangeException(nameof(targetPixelsPerTexel));
        if (!float.IsFinite(hysteresisRatio) ||
            hysteresisRatio is < 0f or >= 0.5f)
            throw new ArgumentOutOfRangeException(nameof(hysteresisRatio));
        TargetPixelsPerTexel = targetPixelsPerTexel;
        HysteresisRatio = hysteresisRatio;
    }

    public float TargetPixelsPerTexel { get; }
    public float HysteresisRatio { get; }
}

/// <summary>
/// Selects a visual LOD from Viewport zoom. Level zero is authoritative Tile data; greater levels
/// are progressively coarser raster Chunks. Switching thresholds use a multiplicative dead band.
/// </summary>
public sealed class TileWorldLodSelector
{
    private readonly int _maximumLevel;
    private readonly float _referenceZoom;
    private readonly float _hysteresis;
    private int? _currentLevel;

    public TileWorldLodSelector(
        TileWorldMetadata metadata,
        TileWorldLodSelectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        TileWorldLodSelectionOptions resolved = options ?? TileWorldLodSelectionOptions.Default;
        _maximumLevel = metadata.DeclaredLodCount - 1;
        _referenceZoom = resolved.TargetPixelsPerTexel * MathF.Min(
            metadata.RasterSettings.Width / metadata.BaseChunkWorldSize.X,
            metadata.RasterSettings.Height / metadata.BaseChunkWorldSize.Y);
        if (!float.IsFinite(_referenceZoom) || _referenceZoom <= 0f)
            throw new ArgumentException("TileWorld metadata produces an invalid LOD reference zoom.", nameof(metadata));
        _hysteresis = resolved.HysteresisRatio;
    }

    public int? CurrentLevel => _currentLevel;
    public int MaximumLevel => _maximumLevel;

    public int Select(float zoom)
    {
        if (!float.IsFinite(zoom) || zoom <= 0f)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        int level = _currentLevel ?? ComputeIdealLevel(zoom);

        while (level < _maximumLevel &&
               zoom < GetBoundaryZoom(level) * (1f - _hysteresis))
            level++;
        while (level > 0 &&
               zoom > GetBoundaryZoom(level - 1) * (1f + _hysteresis))
            level--;

        _currentLevel = level;
        return level;
    }

    public float GetBoundaryZoom(int finerLevel)
    {
        if (finerLevel < 0 || finerLevel >= _maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(finerLevel));
        return MathF.ScaleB(_referenceZoom, -(finerLevel + 1));
    }

    public void Reset(int? level = null)
    {
        if (level is < 0 || level > _maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        _currentLevel = level;
    }

    private int ComputeIdealLevel(float zoom)
    {
        int level = 0;
        while (level < _maximumLevel && zoom <= GetBoundaryZoom(level)) level++;
        return level;
    }
}
