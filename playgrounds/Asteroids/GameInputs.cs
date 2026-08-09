namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Input;

public static class GameInputs
{
    public static readonly InputActionRef TurnLeft = new("player.turn-left");
    public static readonly InputActionRef TurnRight = new("player.turn-right");
    public static readonly InputActionRef Thrust = new("player.thrust");
    public static readonly InputActionRef Fire = new("player.fire");
    public static readonly InputActionRef Pause = new("game.pause");
    public static readonly InputActionRef Restart = new("game.restart");
}
