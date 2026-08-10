namespace FlappyBirdPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

public readonly record struct ScoreGateSpawnArgs(
    Vector2D Position,
    float Width,
    float Height,
    float Speed);

public sealed class ScoreGate : GameInstance
{
    private readonly float _speed;

    public ScoreGate(in ScoreGateSpawnArgs spawn)
    {
        Position = spawn.Position;
        _speed = spawn.Speed;
        Collider = CollisionShape2D.Box(spawn.Width, spawn.Height);
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(new Vector2D(-_speed * (float)deltaTime, 0f));
        if (Position.X < -32f) DestroySelf();
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        // Gameplay-only trigger. Keeping it as an Instance makes scoring use the same collision,
        // Prefab and lifetime model as ordinary world objects.
    }
}
