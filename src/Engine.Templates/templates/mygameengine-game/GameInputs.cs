namespace MyGameTemplate;

using GameEngine.Core.Domain.Input;

public static class GameInputs
{
    public static readonly InputAxis2DRef Move = new("player.move");
    public static readonly InputActionRef Fire = new("player.fire");
}
