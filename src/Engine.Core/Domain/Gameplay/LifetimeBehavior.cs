namespace GameEngine.Core.Domain.Gameplay;

/// <summary>Requests owner destruction after a duration in the owner's selected time domain.</summary>
public sealed class LifetimeBehavior : GameplayBehavior
{
    private double _remainingSeconds;

    public double DurationSeconds { get; }
    public double RemainingSeconds => _remainingSeconds;
    public bool IsCompleted { get; private set; }

    public LifetimeBehavior(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds,
                "Lifetime duration must be finite and non-negative.");
        DurationSeconds = durationSeconds;
        _remainingSeconds = durationSeconds;
    }

    public override void OnCreate()
    {
        _remainingSeconds = DurationSeconds;
        IsCompleted = false;
    }

    public override void OnStep(double deltaTime)
    {
        if (IsCompleted) return;
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime), deltaTime,
                "Delta time must be finite and non-negative.");
        _remainingSeconds = Math.Max(0d, _remainingSeconds - deltaTime);
        if (_remainingSeconds > 0d) return;
        IsCompleted = true;
        DestroyOwner();
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("duration", DurationSeconds);
        writer.Write("remaining", RemainingSeconds);
        writer.Write("completed", IsCompleted);
    }
}
