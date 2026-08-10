namespace FlappyBirdPlayground;

using GameEngine.Core.Domain.Input;

public static class GameInputs
{
    public static readonly InputActionRef Flap = new("bird.flap");
    public static readonly InputActionRef Restart = new("game.restart");
}
