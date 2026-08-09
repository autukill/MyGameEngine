namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

internal sealed class NullInputProvider : IInputProvider
{
    public static NullInputProvider Instance { get; } = new();

    private NullInputProvider() { }

    public bool IsKeyDown(InputKey key) => false;
    public bool WasKeyPressed(InputKey key) => false;
    public bool WasKeyReleased(InputKey key) => false;
    public Vector2D MousePosition => Vector2D.Zero;
    public float MouseScrollDelta => 0f;
    public bool IsMouseButtonDown(MouseButton button) => false;
}
