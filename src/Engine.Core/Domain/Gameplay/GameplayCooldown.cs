namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Tracks a reusable gameplay action cooldown in the time domain chosen by its owner.
/// The owner advances it from OnStep, so pause, time scaling, and unscaled-time behavior
/// remain explicit and require no global timer service.
/// </summary>
public sealed class GameplayCooldown
{
    private double _remainingSeconds;

    public double DurationSeconds { get; }
    public double RemainingSeconds => _remainingSeconds;
    public bool IsReady => _remainingSeconds <= 0d;

    /// <summary>
    /// Gets normalized recovery progress: zero immediately after use and one when ready.
    /// A zero-duration cooldown is always fully recovered.
    /// </summary>
    public double Progress => DurationSeconds == 0d
        ? 1d
        : 1d - (_remainingSeconds / DurationSeconds);

    public GameplayCooldown(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds), durationSeconds,
                "Cooldown duration must be finite and non-negative.");
        DurationSeconds = durationSeconds;
    }

    /// <summary>Advances recovery by a non-negative amount of owner time.</summary>
    public void Update(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime), deltaTime,
                "Delta time must be finite and non-negative.");
        if (_remainingSeconds > 0d)
            _remainingSeconds = Math.Max(0d, _remainingSeconds - deltaTime);
    }

    /// <summary>
    /// Starts the cooldown when ready. Returns false without changing state while recovering.
    /// A zero-duration cooldown deliberately succeeds on every call.
    /// </summary>
    public bool TryUse()
    {
        if (!IsReady) return false;
        _remainingSeconds = DurationSeconds;
        return true;
    }

    /// <summary>Starts the full cooldown regardless of its current state.</summary>
    public void Restart() => _remainingSeconds = DurationSeconds;

    /// <summary>Makes the cooldown immediately ready.</summary>
    public void Reset() => _remainingSeconds = 0d;
}
