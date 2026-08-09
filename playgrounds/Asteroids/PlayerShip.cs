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
    private const float FireInterval = 0.14f;

    private readonly float _worldWidth;
    private readonly float _worldHeight;
    private Vector2D _velocity;
    private float _fireCooldown;
    private double _survivalSeconds;
    private int _shotsFired;

    public PlayerShip(SpriteRef sprite, Vector2D position, float worldWidth, float worldHeight)
    {
        Sprite = sprite;
        Position = position;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        Collider = CollisionShape2D.Circle(24f);
    }

    public override void OnStep(double deltaTime)
    {
        _survivalSeconds += deltaTime;
        float dt = (float)deltaTime;
        float turn = (KeyDown(InputKey.Right) || KeyDown(InputKey.D) ? 1f : 0f) -
                     (KeyDown(InputKey.Left) || KeyDown(InputKey.A) ? 1f : 0f);
        RotateBy(turn * TurnSpeed * dt);

        Vector2D forward = Forward();
        if (KeyDown(InputKey.Up) || KeyDown(InputKey.W))
            _velocity += forward * (Thrust * dt);
        _velocity = _velocity * MathF.Pow(0.35f, dt);
        MoveBy(_velocity * dt);
        WrapAround();

        _fireCooldown = MathF.Max(0f, _fireCooldown - dt);
        if (KeyDown(InputKey.Space) && _fireCooldown <= 0f)
        {
            var spawn = new LaserSpawnArgs(
                Position + forward * 38f,
                _velocity + forward * LaserSpeed);
            Spawn(LaserPrefab, spawn);
            _shotsFired++;
            _fireCooldown = FireInterval;
        }

        if (FirstCollision<Asteroid>() is not null)
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
}
