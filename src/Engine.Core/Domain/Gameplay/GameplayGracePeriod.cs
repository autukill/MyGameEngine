namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Keeps a gameplay condition valid for a short period after it becomes false. Typical uses are
/// coyote-time jumps, recent target visibility, and forgiving interaction ranges.
/// </summary>
public sealed class GameplayGracePeriod
{
    private double _remainingSeconds;

    public double DurationSeconds { get; }
    public double RemainingSeconds => _remainingSeconds;
    public bool IsOpen => _remainingSeconds > 0d;

    public GameplayGracePeriod(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0d)
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds,
                "Grace period duration must be finite and positive.");
        DurationSeconds = durationSeconds;
    }

    /// <summary>Refreshes while the condition is true; otherwise ages the previous observation.</summary>
    public void Update(bool condition, double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime), deltaTime,
                "Delta time must be finite and non-negative.");
        if (condition)
        {
            _remainingSeconds = DurationSeconds;
            return;
        }
        if (_remainingSeconds > 0d)
            _remainingSeconds = Math.Max(0d, _remainingSeconds - deltaTime);
    }

    public void Clear() => _remainingSeconds = 0d;
}
