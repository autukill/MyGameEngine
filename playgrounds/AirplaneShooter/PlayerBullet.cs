namespace AirplaneShooter;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class PlayerBullet : GameInstance
{
    private const float Speed = 620f;

    public PlayerBullet(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Color = new(0.35f, 0.95f, 1f, 1f);
        Collider = CollisionShape2D.Box(8f, 24f);
        UseBehavior(new LifetimeBehavior(1.5d));
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(new Vector2D(0f, -Speed * (float)deltaTime));
        if (FirstCollision<Target>() is not { } target) return;
        Destroy(target);
        DestroySelf();
        SwitchScene(GameScenes.Victory);
    }
}
