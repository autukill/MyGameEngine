namespace GameEngine.Core.Domain.Gameplay;

/// <summary>Immutable time values shared by every participant in one simulation Step.</summary>
public readonly record struct SimulationClockSnapshot(
    ulong StepIndex,
    double UnscaledDeltaSeconds,
    double GameplayDeltaSeconds,
    double UnscaledElapsedSeconds,
    double GameplayElapsedSeconds,
    double TimeScale,
    bool IsPaused);

/// <summary>
/// Read-only simulation timeline advanced by SceneAggregate. Given the same Step delta, pause, and
/// time-scale sequence, it produces the same Tick and elapsed-time sequence without wall-clock IO.
/// </summary>
public sealed class SimulationClock
{
    public SimulationClockSnapshot Current { get; private set; } =
        new(0, 0d, 0d, 0d, 0d, 1d, false);

    public ulong StepIndex => Current.StepIndex;
    public double UnscaledDeltaSeconds => Current.UnscaledDeltaSeconds;
    public double GameplayDeltaSeconds => Current.GameplayDeltaSeconds;
    public double UnscaledElapsedSeconds => Current.UnscaledElapsedSeconds;
    public double GameplayElapsedSeconds => Current.GameplayElapsedSeconds;
    public double TimeScale => Current.TimeScale;
    public bool IsPaused => Current.IsPaused;

    internal SimulationClockSnapshot Advance(in GameplayTimeSnapshot time)
    {
        if (Current.StepIndex == ulong.MaxValue)
            throw new InvalidOperationException("Simulation step index is exhausted.");
        double unscaledElapsed = Current.UnscaledElapsedSeconds + time.UnscaledDeltaTime;
        double gameplayElapsed = Current.GameplayElapsedSeconds + time.DeltaTime;
        if (!double.IsFinite(unscaledElapsed) || !double.IsFinite(gameplayElapsed))
            throw new InvalidOperationException("Simulation elapsed time is exhausted.");
        Current = new SimulationClockSnapshot(
            Current.StepIndex + 1,
            time.UnscaledDeltaTime,
            time.DeltaTime,
            unscaledElapsed,
            gameplayElapsed,
            time.TimeScale,
            time.IsPaused);
        return Current;
    }
}
