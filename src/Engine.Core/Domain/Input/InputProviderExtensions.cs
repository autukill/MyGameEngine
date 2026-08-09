namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

public static class InputProviderExtensions
{
    /// <summary>Returns an unnormalized digital axis in the range [-1, 1] for each component.</summary>
    public static Vector2D Axis2D(
        this IInputProvider input,
        InputKey left = InputKey.A,
        InputKey right = InputKey.D,
        InputKey up = InputKey.W,
        InputKey down = InputKey.S)
    {
        ArgumentNullException.ThrowIfNull(input);
        float x = (input.IsKeyDown(right) ? 1f : 0f) -
                  (input.IsKeyDown(left) ? 1f : 0f);
        float y = (input.IsKeyDown(down) ? 1f : 0f) -
                  (input.IsKeyDown(up) ? 1f : 0f);
        return new Vector2D(x, y);
    }
}
