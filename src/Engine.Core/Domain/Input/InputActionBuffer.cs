namespace GameEngine.Core.Domain.Input;

/// <summary>
/// Remembers a short-lived logical action press until gameplay is ready to consume it.
/// The owner updates this object once per logical Step, so pause and time scaling naturally follow
/// the owning instance's time domain.
/// </summary>
public sealed class InputActionBuffer
{
    private double _remainingSeconds;

    public InputActionRef Action { get; }
    public double WindowSeconds { get; }
    public double RemainingSeconds => _remainingSeconds;
    public bool IsBuffered => _remainingSeconds > 0d;

    public InputActionBuffer(InputActionRef action, double windowSeconds)
    {
        if (action.IsEmpty)
            throw new ArgumentException("Input action reference cannot be empty.", nameof(action));
        if (!double.IsFinite(windowSeconds) || windowSeconds <= 0d)
            throw new ArgumentOutOfRangeException(
                nameof(windowSeconds), windowSeconds,
                "Input buffer window must be finite and positive.");
        Action = action;
        WindowSeconds = windowSeconds;
    }

    /// <summary>
    /// Ages a previous press, then captures the current press for the full configured window.
    /// Calling with pressed=true therefore remains observable during the current Step even when
    /// deltaTime is longer than the buffer window.
    /// </summary>
    public void Update(bool pressed, double deltaTime)
    {
        ValidateDeltaTime(deltaTime);
        if (_remainingSeconds > 0d)
            _remainingSeconds = Math.Max(0d, _remainingSeconds - deltaTime);
        if (pressed)
            _remainingSeconds = WindowSeconds;
    }

    public bool TryConsume()
    {
        if (!IsBuffered) return false;
        _remainingSeconds = 0d;
        return true;
    }

    public void Clear() => _remainingSeconds = 0d;

    private static void ValidateDeltaTime(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(deltaTime), deltaTime,
                "Delta time must be finite and non-negative.");
    }
}
