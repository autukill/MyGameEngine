namespace GameEngine.Core.Domain.Gameplay;

/// <summary>Aggregated measurements for one gameplay query category.</summary>
public readonly record struct GameplayQueryMetric(
    long QueryCount,
    long CandidateCount,
    long HitCount,
    TimeSpan Elapsed);

/// <summary>
/// Optional query measurements accumulated since statistics were enabled or last reset.
/// </summary>
public readonly record struct GameplayQueryStatisticsSnapshot(
    bool IsEnabled,
    long SampledSteps,
    GameplayQueryMetric Find,
    GameplayQueryMetric Collision,
    GameplayQueryMetric Area,
    GameplayQueryMetric Radius)
{
    public long TotalQueries =>
        Find.QueryCount + Collision.QueryCount + Area.QueryCount + Radius.QueryCount;

    public long TotalCandidates =>
        Find.CandidateCount + Collision.CandidateCount +
        Area.CandidateCount + Radius.CandidateCount;

    public long TotalHits =>
        Find.HitCount + Collision.HitCount + Area.HitCount + Radius.HitCount;

    public TimeSpan TotalElapsed =>
        Find.Elapsed + Collision.Elapsed + Area.Elapsed + Radius.Elapsed;

    public double AverageMillisecondsPerStep => SampledSteps == 0
        ? 0d
        : TotalElapsed.TotalMilliseconds / SampledSteps;
}
