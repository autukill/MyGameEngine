namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Asteroid : GameInstance, IHasGameplayHealth
{
    private readonly Vector2D _velocity;
    private readonly float _worldWidth;
    private readonly float _worldHeight;
    public GameplayHealth Health { get; } = new(2f);

    public Asteroid(SpriteRef sprite, in AsteroidSpawnArgs spawn)
    {
        Sprite = sprite;
        Position = spawn.Position;
        _velocity = spawn.Velocity;
        _worldWidth = spawn.WorldWidth;
        _worldHeight = spawn.WorldHeight;
        float diameterScale = spawn.Radius * 2f / 48f;
        Scale = new Vector2D(diameterScale, diameterScale);
        Rotation = spawn.Position.X * 0.01f;
        Color = new(0.7f, 0.55f, 0.35f, 1f);
        Collider = CollisionShape2D.Circle(spawn.Radius);
        AddTag(GameTags.Enemy);
        AddTag(GameTags.Damageable);
        UseBehavior(new SpinBehavior(0.7f));
    }

    public override void OnStep(double deltaTime)
    {
        float dt = (float)deltaTime;
        MoveBy(_velocity * dt);
        WrapAround();
    }

    private void WrapAround()
    {
        float x = Position.X;
        float y = Position.Y;
        if (x < -40f) x = _worldWidth + 40f;
        else if (x > _worldWidth + 40f) x = -40f;
        if (y < -40f) y = _worldHeight + 40f;
        else if (y > _worldHeight + 40f) y = -40f;
        Position = new Vector2D(x, y);
    }
}
