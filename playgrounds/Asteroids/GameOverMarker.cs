namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

public sealed class GameOverMarker : GameInstance
{
    public GameOverMarker(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Color = new(1f, 0.3f, 0.2f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        RotateBy((float)deltaTime);
        if (KeyPressed(InputKey.Enter))
            SwitchScene(GameScenes.Main);
    }
}
