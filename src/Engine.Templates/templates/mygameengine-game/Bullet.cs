namespace MyGameTemplate;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Bullet : GameInstance
{
    private static readonly AlarmId Lifetime = new("lifetime");
    private const float Speed = 320f;

    public Bullet(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Scale = new Vector2D(.3f, .3f);
    }

    public override void OnCreate() => SetAlarm(Lifetime, 1.5d);

    public override void OnStep(double deltaTime) =>
        MoveBy(new Vector2D(0f, -Speed * (float)deltaTime));

    public override void OnAlarm(AlarmId alarm)
    {
        if (alarm == Lifetime) DestroySelf();
    }
}
