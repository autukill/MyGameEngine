namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

public sealed class PlayerShip : GameInstance
{
    public static readonly PrefabRef<Laser, LaserSpawnArgs> LaserPrefab =
        new("asteroids.laser");

    private const float TurnSpeed = 3.4f;
    private const float Thrust = 260f;
    private const float LaserSpeed = 700f;
    private const double FireInterval = 0.14d;

    private readonly float _worldWidth;
    private readonly float _worldHeight;
    private readonly InputActionBuffer _fireBuffer = new(GameInputs.Fire, 0.12d);
    private readonly GameplayCooldown _fireCooldown = new(FireInterval);
    private Vector2D _velocity;
    private double _survivalSeconds;
    private int _shotsFired;

    public PlayerShip(SpriteRef sprite, Vector2D position, float worldWidth, float worldHeight)
    {
        Sprite = sprite;
        Position = position;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        Collider = CollisionShape2D.Circle(24f);
        AddTag(GameTags.Player);
        AddTag(GameTags.Damageable);
    }

    public override void OnStep(double deltaTime)
    {
        _survivalSeconds += deltaTime;
        float dt = (float)deltaTime;
        float turn = (ActionDown(GameInputs.TurnRight) ? 1f : 0f) -
                     (ActionDown(GameInputs.TurnLeft) ? 1f : 0f);
        RotateBy(turn * TurnSpeed * dt);

        Vector2D forward = Forward();
        if (ActionDown(GameInputs.Thrust))
            _velocity += forward * (Thrust * dt);
        _velocity = _velocity * MathF.Pow(0.35f, dt);
        MoveBy(_velocity * dt);
        WrapAround();

        _fireCooldown.Update(deltaTime);
        UpdateActionBuffer(_fireBuffer, deltaTime);
        if ((ActionDown(GameInputs.Fire) || _fireBuffer.IsBuffered) && _fireCooldown.TryUse())
        {
            var spawn = new LaserSpawnArgs(
                Position + forward * 38f,
                _velocity + forward * LaserSpeed);
            Spawn(LaserPrefab, spawn);
            _shotsFired++;
            _fireBuffer.TryConsume();
        }

        if (FirstCollision(GameTags.Enemy) is not null)
            SwitchScene(GameScenes.GameOver, new GameOverArgs(_survivalSeconds, _shotsFired));
    }

    private Vector2D Forward() => new(MathF.Sin(Rotation), -MathF.Cos(Rotation));

    private void WrapAround()
    {
        float x = Position.X;
        float y = Position.Y;
        if (x < 0f) x += _worldWidth;
        else if (x > _worldWidth) x -= _worldWidth;
        if (y < 0f) y += _worldHeight;
        else if (y > _worldHeight) y -= _worldHeight;
        Position = new Vector2D(x, y);
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("ship.velocity", _velocity);
        writer.Write("ship.survivalSeconds", _survivalSeconds);
        writer.Write("ship.shotsFired", _shotsFired);
        writer.Write("ship.fireBuffer", _fireBuffer);
        writer.Write("ship.fireCooldown", _fireCooldown);
        writer.Write("ship.worldWidth", _worldWidth);
        writer.Write("ship.worldHeight", _worldHeight);
    }
}
