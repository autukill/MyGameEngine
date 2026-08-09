namespace AirplaneShooter;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Target : GameInstance
{
    public Target(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Scale = new Vector2D(6f, 2f);
        Color = new(1f, 0.2f, 0.25f, 1f);
        Collider = GameEngine.Core.Domain.Gameplay.CollisionShape2D.Box(48f, 48f);
    }

    public override void OnStep(double deltaTime) => RotateBy((float)deltaTime);
}
