namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Wraps physical or logical input and can temporarily expose a neutral frame without replacing
/// the underlying provider. Hosting uses it for full-Scene transitions; other systems can reuse it
/// for explicit modal gameplay gates.
/// </summary>
public sealed class InputGateProvider : IInputProvider
{
    private readonly IInputProvider _source;
    internal IInputProvider Source => _source;

    public bool IsBlocked { get; set; }

    public InputGateProvider(IInputProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool IsKeyDown(InputKey key) => !IsBlocked && _source.IsKeyDown(key);
    public bool WasKeyPressed(InputKey key) => !IsBlocked && _source.WasKeyPressed(key);
    public bool WasKeyReleased(InputKey key) => !IsBlocked && _source.WasKeyReleased(key);
    public Vector2D MousePosition => IsBlocked ? Vector2D.Zero : _source.MousePosition;
    public float MouseScrollDelta => IsBlocked ? 0f : _source.MouseScrollDelta;
    public bool IsMouseButtonDown(MouseButton button) =>
        !IsBlocked && _source.IsMouseButtonDown(button);
    public int PointerCount => IsBlocked ? 0 : _source.PointerCount;
    public PointerContact GetPointer(int index) => IsBlocked
        ? throw new ArgumentOutOfRangeException(nameof(index))
        : _source.GetPointer(index);

}
