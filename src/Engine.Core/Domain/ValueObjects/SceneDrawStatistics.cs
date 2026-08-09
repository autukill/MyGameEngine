namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>A zero-allocation snapshot of one Scene draw traversal.</summary>
public readonly record struct SceneDrawStatistics(
    bool TimingEnabled,
    int VisibleLayerCount,
    int CandidateVisitCount,
    int CulledInstanceCount,
    int SelectedInstanceCount,
    int DrawnInstanceCount,
    int SortComparisonCount,
    TimeSpan TraversalTime,
    TimeSpan SortTime,
    TimeSpan DrawTime)
{
    public TimeSpan TotalTime => TraversalTime + SortTime + DrawTime;
}
