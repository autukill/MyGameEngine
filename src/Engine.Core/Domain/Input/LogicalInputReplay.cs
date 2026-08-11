namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

[Flags]
public enum LogicalInputActionState : byte
{
    None = 0,
    Down = 1 << 0,
    Pressed = 1 << 1,
    Released = 1 << 2
}

/// <summary>Immutable logical input values captured for one simulation tick.</summary>
public sealed class LogicalInputFrame
{
    private readonly LogicalInputActionState[] _actions;
    private readonly Vector2D[] _axes;

    public ulong StepIndex { get; }
    public int ActionCount => _actions.Length;
    public int Axis2DCount => _axes.Length;

    internal LogicalInputFrame(
        ulong stepIndex,
        ReadOnlySpan<LogicalInputActionState> actions,
        ReadOnlySpan<Vector2D> axes)
    {
        if (stepIndex == 0)
            throw new ArgumentOutOfRangeException(nameof(stepIndex), "Step index must be positive.");
        StepIndex = stepIndex;
        _actions = actions.ToArray();
        _axes = axes.ToArray();
    }

    public LogicalInputActionState GetActionState(int index) => _actions[index];

    public Vector2D GetAxis2D(int index) => _axes[index];
}

/// <summary>
/// Immutable in-memory logical input stream. Physical device bindings and wall-clock timing are
/// intentionally excluded; the stream must be replayed with the same InputMap and fixed delta.
/// </summary>
public sealed class LogicalInputRecording
{
    public const int CurrentFormatVersion = 1;

    private readonly InputActionRef[] _actions;
    private readonly InputAxis2DRef[] _axes;
    private readonly LogicalInputFrame[] _frames;
    private readonly IReadOnlyList<LogicalInputFrame> _framesView;

    public int FormatVersion => CurrentFormatVersion;
    public double? FixedDeltaSeconds { get; }
    public ReadOnlyMemory<InputActionRef> Actions => _actions;
    public ReadOnlyMemory<InputAxis2DRef> Axes2D => _axes;
    public IReadOnlyList<LogicalInputFrame> Frames => _framesView;
    public int FrameCount => _frames.Length;
    public ulong FirstStepIndex => _frames.Length == 0 ? 0 : _frames[0].StepIndex;
    public ulong LastStepIndex => _frames.Length == 0 ? 0 : _frames[^1].StepIndex;

    internal LogicalInputRecording(
        ReadOnlySpan<InputActionRef> actions,
        ReadOnlySpan<InputAxis2DRef> axes,
        LogicalInputFrame[] frames,
        double? fixedDeltaSeconds)
    {
        _actions = actions.ToArray();
        _axes = axes.ToArray();
        _frames = frames;
        _framesView = Array.AsReadOnly(_frames);
        FixedDeltaSeconds = fixedDeltaSeconds;
    }

    public void ValidateAgainst(InputMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.HasSameLogicalSchema(_actions, _axes))
            throw new InvalidOperationException(
                "The logical input recording does not match the configured InputMap schema.");
    }
}

/// <summary>
/// Captures one immutable logical input frame per simulation tick. Frame storage intentionally
/// allocates while recording; gameplay queries against the current frame remain allocation-free.
/// </summary>
public sealed class LogicalInputRecorder : IInputProvider, ILogicalInputProvider
{
    private readonly List<LogicalInputFrame> _frames;
    private InputMap? _map;
    private LogicalInputActionState[] _actions = [];
    private Vector2D[] _axes = [];
    private ulong _currentStepIndex;
    private double? _fixedDeltaSeconds;

    public int FrameCount => _frames.Count;
    public ulong CurrentStepIndex => _currentStepIndex;

    public LogicalInputRecorder(int initialFrameCapacity = 0)
    {
        if (initialFrameCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialFrameCapacity));
        _frames = new List<LogicalInputFrame>(initialFrameCapacity);
    }

    public LogicalInputRecording Snapshot()
    {
        InputMap map = _map ?? throw new InvalidOperationException(
            "The recorder has not captured an input frame yet.");
        return new LogicalInputRecording(
            map.ActionRefs,
            map.Axis2DRefs,
            _frames.ToArray(),
            _fixedDeltaSeconds);
    }

    /// <summary>Prepares neutral logical values before the first captured simulation Step.</summary>
    public void Prepare(InputMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Bind(map);
    }

    /// <summary>Prepares the recorder with the fixed delta required for full-session replay.</summary>
    public void Prepare(InputMap map, double fixedDeltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d)
            throw new ArgumentOutOfRangeException(
                nameof(fixedDeltaSeconds), fixedDeltaSeconds,
                "Fixed delta must be finite and positive.");
        Bind(map);
        if (_fixedDeltaSeconds is { } existing && existing != fixedDeltaSeconds)
            throw new InvalidOperationException(
                "One LogicalInputRecorder cannot use different fixed delta values.");
        _fixedDeltaSeconds = fixedDeltaSeconds;
    }

    public void BeginStep(ulong stepIndex, InputMap map, IInputProvider physicalInput)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(physicalInput);
        if (stepIndex == 0 ||
            _currentStepIndex == ulong.MaxValue ||
            (_currentStepIndex != 0 && stepIndex != _currentStepIndex + 1UL))
        {
            string expected = _currentStepIndex == 0
                ? "a positive first Step"
                : $"Step {_currentStepIndex + 1UL}";
            throw new InvalidOperationException(
                $"Logical input recording expected {expected}, " +
                $"but received Step {stepIndex}.");
        }

        Bind(map);
        map.CaptureLogicalFrame(physicalInput, _actions, _axes);
        _frames.Add(new LogicalInputFrame(stepIndex, _actions, _axes));
        _currentStepIndex = stepIndex;
    }

    bool ILogicalInputProvider.ActionDown(InputActionRef action) =>
        (_actions[RequireMap().GetActionIndex(action)] & LogicalInputActionState.Down) != 0;

    bool ILogicalInputProvider.ActionPressed(InputActionRef action) =>
        (_actions[RequireMap().GetActionIndex(action)] & LogicalInputActionState.Pressed) != 0;

    bool ILogicalInputProvider.ActionReleased(InputActionRef action) =>
        (_actions[RequireMap().GetActionIndex(action)] & LogicalInputActionState.Released) != 0;

    Vector2D ILogicalInputProvider.Axis2D(InputAxis2DRef axis) =>
        _axes[RequireMap().GetAxis2DIndex(axis)];

    private void Bind(InputMap map)
    {
        if (_map is null)
        {
            _map = map;
            _actions = new LogicalInputActionState[map.ActionCount];
            _axes = new Vector2D[map.Axis2DCount];
            return;
        }
        if (!_map.HasSameLogicalSchema(map.ActionRefs, map.Axis2DRefs))
            throw new InvalidOperationException(
                "One LogicalInputRecorder cannot capture different InputMap schemas.");
    }

    private InputMap RequireMap() => _map ?? throw new InvalidOperationException(
        "No logical input frame is active.");

    public bool IsKeyDown(InputKey key) => throw RawInputNotReplayable();
    public bool WasKeyPressed(InputKey key) => throw RawInputNotReplayable();
    public bool WasKeyReleased(InputKey key) => throw RawInputNotReplayable();
    public Vector2D MousePosition => throw RawInputNotReplayable();
    public float MouseScrollDelta => throw RawInputNotReplayable();
    public bool IsMouseButtonDown(MouseButton button) => throw RawInputNotReplayable();
    public int PointerCount => throw RawInputNotReplayable();
    public PointerContact GetPointer(int index) => throw RawInputNotReplayable();

    internal static InvalidOperationException RawInputNotReplayable() => new(
        "Physical Key/Mouse input is not available during logical input recording or playback. " +
        "Use configured logical Action and Axis queries for deterministic gameplay.");
}

/// <summary>Feeds a recorded logical frame into ordinary InputMap queries one tick at a time.</summary>
public sealed class LogicalInputPlayback : IInputProvider, ILogicalInputProvider
{
    private readonly LogicalInputRecording _recording;
    private readonly InputMap _map;
    private int _nextFrameIndex;
    private LogicalInputFrame? _current;

    public ulong CurrentStepIndex => _current?.StepIndex ?? 0;
    public bool IsComplete => _nextFrameIndex == _recording.FrameCount;

    public LogicalInputPlayback(LogicalInputRecording recording, InputMap map)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        recording.ValidateAgainst(map);
    }

    public void BeginStep(ulong stepIndex)
    {
        if (_nextFrameIndex >= _recording.FrameCount)
            throw new InvalidOperationException(
                $"Logical input playback has no frame for Step {stepIndex}.");
        LogicalInputFrame next = _recording.Frames[_nextFrameIndex];
        if (next.StepIndex != stepIndex)
            throw new InvalidOperationException(
                $"Logical input playback expected recorded Step {next.StepIndex}, " +
                $"but simulation requested Step {stepIndex}.");
        _current = next;
        _nextFrameIndex++;
    }

    bool ILogicalInputProvider.ActionDown(InputActionRef action) =>
        (CurrentActionState(_map.GetActionIndex(action)) &
         LogicalInputActionState.Down) != 0;

    bool ILogicalInputProvider.ActionPressed(InputActionRef action) =>
        (CurrentActionState(_map.GetActionIndex(action)) &
         LogicalInputActionState.Pressed) != 0;

    bool ILogicalInputProvider.ActionReleased(InputActionRef action) =>
        (CurrentActionState(_map.GetActionIndex(action)) &
         LogicalInputActionState.Released) != 0;

    Vector2D ILogicalInputProvider.Axis2D(InputAxis2DRef axis) =>
        _current?.GetAxis2D(_map.GetAxis2DIndex(axis)) ?? Vector2D.Zero;

    private LogicalInputActionState CurrentActionState(int index) =>
        _current?.GetActionState(index) ?? LogicalInputActionState.None;

    public bool IsKeyDown(InputKey key) => throw LogicalInputRecorder.RawInputNotReplayable();
    public bool WasKeyPressed(InputKey key) => throw LogicalInputRecorder.RawInputNotReplayable();
    public bool WasKeyReleased(InputKey key) => throw LogicalInputRecorder.RawInputNotReplayable();
    public Vector2D MousePosition => throw LogicalInputRecorder.RawInputNotReplayable();
    public float MouseScrollDelta => throw LogicalInputRecorder.RawInputNotReplayable();
    public bool IsMouseButtonDown(MouseButton button) =>
        throw LogicalInputRecorder.RawInputNotReplayable();
    public int PointerCount => throw LogicalInputRecorder.RawInputNotReplayable();
    public PointerContact GetPointer(int index) =>
        throw LogicalInputRecorder.RawInputNotReplayable();
}

internal interface ILogicalInputProvider
{
    bool ActionDown(InputActionRef action);
    bool ActionPressed(InputActionRef action);
    bool ActionReleased(InputActionRef action);
    Vector2D Axis2D(InputAxis2DRef axis);
}
