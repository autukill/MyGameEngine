namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Laser : GameInstance
{
    private static readonly AlarmId Lifetime = new("lifetime");
    private readonly Vector2D _velocity;

    public Laser(SpriteRef sprite, in LaserSpawnArgs spawn)
    {
        Sprite = sprite;
        Position = spawn.Position;
        _velocity = spawn.Velocity;
        Rotation = MathF.Atan2(_velocity.X, -_velocity.Y);
        Color = new(0.35f, 1f, 0.85f, 1f);
        Collider = CollisionShape2D.Circle(5f);
    }

    public override void OnCreate() => SetAlarm(Lifetime, 1.1d);

    public override void OnStep(double deltaTime)
    {
        MoveBy(_velocity * (float)deltaTime);
        if (FirstCollision<Asteroid>() is not { } asteroid) return;
        Destroy(asteroid);
        DestroySelf();
    }

    public override void OnAlarm(AlarmId alarm)
    {
        if (alarm == Lifetime) DestroySelf();
    }
}
