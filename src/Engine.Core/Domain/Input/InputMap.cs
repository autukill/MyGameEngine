namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Immutable logical input bindings. Query methods read a caller-supplied physical provider and
/// perform no per-frame allocation.
/// </summary>
public sealed class InputMap
{
    private static readonly Dictionary<InputActionRef, InputKey[]> NoActions = [];
    private static readonly Dictionary<InputAxis2DRef, DigitalAxis2DBinding[]> NoAxes = [];
    private readonly IReadOnlyDictionary<InputActionRef, InputKey[]> _actions;
    private readonly IReadOnlyDictionary<InputAxis2DRef, DigitalAxis2DBinding[]> _axes;
    private readonly InputActionRef[] _actionRefs;
    private readonly InputAxis2DRef[] _axis2DRefs;
    private readonly Dictionary<InputActionRef, int> _actionIndices;
    private readonly Dictionary<InputAxis2DRef, int> _axis2DIndices;
    private readonly bool _strict;

    public static InputMap Empty { get; } = new(NoActions, NoAxes, strict: false);

    public int ActionCount => _actions.Count;
    public int Axis2DCount => _axes.Count;
    public bool IsEmpty => ActionCount == 0 && Axis2DCount == 0;
    internal ReadOnlySpan<InputActionRef> ActionRefs => _actionRefs;
    internal ReadOnlySpan<InputAxis2DRef> Axis2DRefs => _axis2DRefs;

    internal InputMap(
        IReadOnlyDictionary<InputActionRef, InputKey[]> actions,
        IReadOnlyDictionary<InputAxis2DRef, DigitalAxis2DBinding[]> axes,
        bool strict = true)
    {
        _actions = actions;
        _axes = axes;
        _strict = strict;
        _actionRefs = actions.Keys.OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray();
        _axis2DRefs = axes.Keys.OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray();
        _actionIndices = new Dictionary<InputActionRef, int>(_actionRefs.Length);
        _axis2DIndices = new Dictionary<InputAxis2DRef, int>(_axis2DRefs.Length);
        for (int i = 0; i < _actionRefs.Length; i++) _actionIndices.Add(_actionRefs[i], i);
        for (int i = 0; i < _axis2DRefs.Length; i++) _axis2DIndices.Add(_axis2DRefs[i], i);
    }

    public bool IsActionDefined(InputActionRef action) =>
        !action.IsEmpty && _actions.ContainsKey(action);

    public bool IsAxis2DDefined(InputAxis2DRef axis) =>
        !axis.IsEmpty && _axes.ContainsKey(axis);

    public bool ActionDown(IInputProvider input, InputActionRef action)
    {
        ArgumentNullException.ThrowIfNull(input);
        InputKey[]? keys = RequireAction(action);
        if (keys is null) return false;
        if (input is ILogicalInputProvider logical) return logical.ActionDown(action);
        for (int i = 0; i < keys.Length; i++)
        {
            if (input.IsKeyDown(keys[i])) return true;
        }
        return false;
    }

    public bool ActionPressed(IInputProvider input, InputActionRef action)
    {
        ArgumentNullException.ThrowIfNull(input);
        InputKey[]? keys = RequireAction(action);
        if (keys is null) return false;
        if (input is ILogicalInputProvider logical) return logical.ActionPressed(action);
        for (int i = 0; i < keys.Length; i++)
        {
            if (input.WasKeyPressed(keys[i])) return true;
        }
        return false;
    }

    public bool ActionReleased(IInputProvider input, InputActionRef action)
    {
        ArgumentNullException.ThrowIfNull(input);
        InputKey[]? keys = RequireAction(action);
        if (keys is null) return false;
        if (input is ILogicalInputProvider logical) return logical.ActionReleased(action);
        for (int i = 0; i < keys.Length; i++)
        {
            if (input.WasKeyReleased(keys[i])) return true;
        }
        return false;
    }

    public Vector2D Axis2D(IInputProvider input, InputAxis2DRef axis)
    {
        ArgumentNullException.ThrowIfNull(input);
        DigitalAxis2DBinding[]? bindings = RequireAxis(axis);
        if (bindings is null) return Vector2D.Zero;
        if (input is ILogicalInputProvider logical) return logical.Axis2D(axis);

        float x = 0f;
        float y = 0f;
        for (int i = 0; i < bindings.Length; i++)
        {
            DigitalAxis2DBinding binding = bindings[i];
            x += (input.IsKeyDown(binding.Right) ? 1f : 0f) -
                 (input.IsKeyDown(binding.Left) ? 1f : 0f);
            y += (input.IsKeyDown(binding.Down) ? 1f : 0f) -
                 (input.IsKeyDown(binding.Up) ? 1f : 0f);
        }
        return new Vector2D(Math.Clamp(x, -1f, 1f), Math.Clamp(y, -1f, 1f));
    }

    private InputKey[]? RequireAction(InputActionRef action)
    {
        if (action.IsEmpty)
            throw new ArgumentException("Input action reference cannot be empty.", nameof(action));
        if (_actions.TryGetValue(action, out InputKey[]? keys)) return keys;
        if (_strict)
            throw new KeyNotFoundException($"Input action '{action}' is not configured.");
        return null;
    }

    private DigitalAxis2DBinding[]? RequireAxis(InputAxis2DRef axis)
    {
        if (axis.IsEmpty)
            throw new ArgumentException("Input axis reference cannot be empty.", nameof(axis));
        if (_axes.TryGetValue(axis, out DigitalAxis2DBinding[]? bindings)) return bindings;
        if (_strict)
            throw new KeyNotFoundException($"Input axis '{axis}' is not configured.");
        return null;
    }

    internal int GetActionIndex(InputActionRef action) =>
        _actionIndices.TryGetValue(action, out int index)
            ? index
            : throw new KeyNotFoundException($"Input action '{action}' is not configured.");

    internal int GetAxis2DIndex(InputAxis2DRef axis) =>
        _axis2DIndices.TryGetValue(axis, out int index)
            ? index
            : throw new KeyNotFoundException($"Input axis '{axis}' is not configured.");

    internal bool HasSameLogicalSchema(
        ReadOnlySpan<InputActionRef> actions,
        ReadOnlySpan<InputAxis2DRef> axes)
    {
        if (!_actionRefs.AsSpan().SequenceEqual(actions) ||
            !_axis2DRefs.AsSpan().SequenceEqual(axes)) return false;
        return true;
    }

    internal void CaptureLogicalFrame(
        IInputProvider input,
        Span<LogicalInputActionState> actionStates,
        Span<Vector2D> axes)
    {
        if (actionStates.Length != _actionRefs.Length || axes.Length != _axis2DRefs.Length)
            throw new ArgumentException("Logical input frame storage does not match the InputMap.");
        for (int i = 0; i < _actionRefs.Length; i++)
        {
            InputActionRef action = _actionRefs[i];
            LogicalInputActionState state = LogicalInputActionState.None;
            if (ActionDown(input, action)) state |= LogicalInputActionState.Down;
            if (ActionPressed(input, action)) state |= LogicalInputActionState.Pressed;
            if (ActionReleased(input, action)) state |= LogicalInputActionState.Released;
            actionStates[i] = state;
        }
        for (int i = 0; i < _axis2DRefs.Length; i++)
            axes[i] = Axis2D(input, _axis2DRefs[i]);
    }

}

internal readonly record struct DigitalAxis2DBinding(
    InputKey Left,
    InputKey Right,
    InputKey Up,
    InputKey Down);
