namespace AirplaneShooter;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public sealed class VictoryMarker : GameInstance
{
    public VictoryMarker(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Color = new(0.35f, 1f, 0.55f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        RotateBy((float)deltaTime);
        if (ActionPressed(GameInputs.Restart))
            SwitchScene(GameScenes.Main);
    }
}
