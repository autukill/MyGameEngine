namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>A stable logical reason for pausing Gameplay simulation.</summary>
public readonly record struct GameplayPauseKey
{
    public string Name { get; }

    public GameplayPauseKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

/// <summary>Selects whether an Instance follows Gameplay time or real update time.</summary>
public enum InstanceTimeMode
{
    Gameplay,
    Unscaled
}

/// <summary>Read-only time values for the most recently started Scene update.</summary>
public readonly record struct GameplayTimeSnapshot(
    double UnscaledDeltaTime,
    double DeltaTime,
    double TimeScale,
    bool IsPaused);

/// <summary>
/// Scene time state shared by Hosting and GameInstances. Pause requests are owner-aware so one
/// system cannot accidentally resume another system's pause.
/// </summary>
public sealed class GameplayTimeController
{
    private const double MaximumTimeScale = 8d;
    private readonly HashSet<GameplayPauseKey> _externalPauses = [];
    private readonly HashSet<InstancePauseRequest> _instancePauses = [];
    private double _timeScale = 1d;

    public double TimeScale
    {
        get => _timeScale;
        set
        {
            if (!double.IsFinite(value) || value <= 0d || value > MaximumTimeScale)
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, $"Time scale must be finite and in (0, {MaximumTimeScale}].");
            _timeScale = value;
            RefreshCurrent();
        }
    }

    public bool IsPaused => _externalPauses.Count > 0 || _instancePauses.Count > 0;

    public int PauseRequestCount => _externalPauses.Count + _instancePauses.Count;

    public GameplayTimeSnapshot Current { get; private set; } = new(0d, 0d, 1d, false);

    /// <summary>Adds an external/Hosting-owned pause reason. Repeated keys are idempotent.</summary>
    public void Pause(GameplayPauseKey key)
    {
        Validate(key);
        if (_externalPauses.Add(key)) RefreshCurrent();
    }

    public void Resume(GameplayPauseKey key)
    {
        Validate(key);
        if (_externalPauses.Remove(key)) RefreshCurrent();
    }

    public void Toggle(GameplayPauseKey key)
    {
        Validate(key);
        if (!_externalPauses.Remove(key)) _externalPauses.Add(key);
        RefreshCurrent();
    }

    internal void Pause(InstanceId owner, GameplayPauseKey key)
    {
        Validate(key);
        if (_instancePauses.Add(new InstancePauseRequest(owner, key))) RefreshCurrent();
    }

    internal void Resume(InstanceId owner, GameplayPauseKey key)
    {
        Validate(key);
        if (_instancePauses.Remove(new InstancePauseRequest(owner, key))) RefreshCurrent();
    }

    internal void Toggle(InstanceId owner, GameplayPauseKey key)
    {
        Validate(key);
        var request = new InstancePauseRequest(owner, key);
        if (!_instancePauses.Remove(request)) _instancePauses.Add(request);
        RefreshCurrent();
    }

    internal void ReleaseOwner(InstanceId owner)
    {
        if (_instancePauses.RemoveWhere(request => request.Owner == owner) > 0)
            RefreshCurrent();
    }

    internal void ResetSceneState()
    {
        _instancePauses.Clear();
        _timeScale = 1d;
        RefreshCurrent();
    }

    internal GameplayTimeSnapshot BeginFrame(double unscaledDeltaTime)
    {
        if (!double.IsFinite(unscaledDeltaTime) || unscaledDeltaTime < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(unscaledDeltaTime), unscaledDeltaTime,
                "Unscaled delta time must be finite and non-negative.");
        Current = CreateSnapshot(unscaledDeltaTime);
        return Current;
    }

    private GameplayTimeSnapshot CreateSnapshot(double unscaledDeltaTime)
    {
        bool paused = IsPaused;
        return new GameplayTimeSnapshot(
            unscaledDeltaTime,
            paused ? 0d : unscaledDeltaTime * _timeScale,
            _timeScale,
            paused);
    }

    private void RefreshCurrent() => Current = CreateSnapshot(Current.UnscaledDeltaTime);

    private static void Validate(GameplayPauseKey key)
    {
        if (key.IsEmpty)
            throw new ArgumentException("Pause key cannot be empty.", nameof(key));
    }

    private readonly record struct InstancePauseRequest(
        InstanceId Owner,
        GameplayPauseKey Key);
}
