namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Laser : GameInstance
{
    private readonly Vector2D _velocity;

    public Laser(SpriteRef sprite, in LaserSpawnArgs spawn)
    {
        Sprite = sprite;
        Position = spawn.Position;
        _velocity = spawn.Velocity;
        Rotation = MathF.Atan2(_velocity.X, -_velocity.Y);
        Color = new(0.35f, 1f, 0.85f, 1f);
        Collider = CollisionShape2D.Circle(5f);
        AddTag(GameTags.PlayerProjectile);
        UseBehavior(new LifetimeBehavior(1.1d));
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(_velocity * (float)deltaTime);
        if (FirstCollision(GameTags.Enemy) is not { } enemy) return;
        DestroySelf();
        if (enemy is IHasGameplayHealth damageable &&
            damageable.Health.ApplyDamage(1f).BecameDepleted)
        {
            var destroyed = new AsteroidDestroyedSignal(enemy.Position, Score: 100);
            PublishSignal(in destroyed);
            Destroy(enemy);
        }
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer) =>
        writer.Write("laser.velocity", _velocity);
}
