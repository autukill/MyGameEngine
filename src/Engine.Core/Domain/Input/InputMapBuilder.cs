namespace GameEngine.Core.Domain.Input;

/// <summary>Builds an immutable logical input map during application composition.</summary>
public sealed class InputMapBuilder
{
    private readonly Dictionary<InputActionRef, List<InputKey>> _actions = [];
    private readonly Dictionary<InputAxis2DRef, List<DigitalAxis2DBinding>> _axes = [];
    private readonly Dictionary<string, InputControlKind> _kinds = new(StringComparer.Ordinal);

    public InputMapBuilder BindAction(InputActionRef action, params InputKey[] keys)
    {
        if (action.IsEmpty)
            throw new ArgumentException("Input action reference cannot be empty.", nameof(action));
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0)
            throw new ArgumentException("An input action requires at least one key.", nameof(keys));
        RegisterKind(action.Name, InputControlKind.Action);
        if (!_actions.TryGetValue(action, out List<InputKey>? bindings))
        {
            bindings = [];
            _actions.Add(action, bindings);
        }
        for (int i = 0; i < keys.Length; i++)
        {
            InputKey key = ValidateKey(keys[i], nameof(keys));
            if (bindings.Contains(key))
                throw new ArgumentException(
                    $"Input action '{action}' already binds key '{key}'.", nameof(keys));
            bindings.Add(key);
        }
        return this;
    }

    public InputMapBuilder BindAxis2D(
        InputAxis2DRef axis,
        InputKey left,
        InputKey right,
        InputKey up,
        InputKey down)
    {
        if (axis.IsEmpty)
            throw new ArgumentException("Input axis reference cannot be empty.", nameof(axis));
        var binding = new DigitalAxis2DBinding(
            ValidateKey(left, nameof(left)),
            ValidateKey(right, nameof(right)),
            ValidateKey(up, nameof(up)),
            ValidateKey(down, nameof(down)));
        if (binding.Left == binding.Right || binding.Left == binding.Up ||
            binding.Left == binding.Down || binding.Right == binding.Up ||
            binding.Right == binding.Down || binding.Up == binding.Down)
        {
            throw new ArgumentException("A digital 2D axis requires four distinct keys.");
        }
        RegisterKind(axis.Name, InputControlKind.Axis2D);
        if (!_axes.TryGetValue(axis, out List<DigitalAxis2DBinding>? bindings))
        {
            bindings = [];
            _axes.Add(axis, bindings);
        }
        if (bindings.Contains(binding))
            throw new ArgumentException($"Input axis '{axis}' already contains this binding.");
        bindings.Add(binding);
        return this;
    }

    public InputMap Build()
    {
        if (_actions.Count == 0 && _axes.Count == 0) return InputMap.Empty;
        var actions = _actions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray());
        var axes = _axes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray());
        return new InputMap(actions, axes);
    }

    private void RegisterKind(string name, InputControlKind kind)
    {
        if (_kinds.TryGetValue(name, out InputControlKind existing) && existing != kind)
            throw new ArgumentException(
                $"Input control '{name}' is already configured as {existing}.");
        _kinds[name] = kind;
    }

    private static InputKey ValidateKey(InputKey key, string parameterName)
    {
        if (!Enum.IsDefined(key) || key == InputKey.None)
            throw new ArgumentOutOfRangeException(parameterName, "Input key cannot be None.");
        return key;
    }

    private enum InputControlKind
    {
        Action,
        Axis2D
    }
}
