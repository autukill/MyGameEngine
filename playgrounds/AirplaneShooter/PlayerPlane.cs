namespace AirplaneShooter;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

public sealed class PlayerPlane : GameInstance
{
    private const float MoveSpeed = 360f;
    private const float FireInterval = 0.12f;
    private const float HalfSize = 40f;

    private readonly SpriteRef _bulletSprite;
    private readonly float _worldWidth;
    private readonly float _worldHeight;
    private float _fireCooldown;

    public PlayerPlane(
        SpriteRef sprite,
        SpriteRef bulletSprite,
        Vector2D position,
        float worldWidth,
        float worldHeight)
    {
        Sprite = sprite;
        _bulletSprite = bulletSprite;
        Position = position;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
    }

    public override void OnStep(double deltaTime)
    {
        float dt = (float)deltaTime;
        Vector2D direction = InputAxis2D(
            InputKey.Left,
            InputKey.Right,
            InputKey.Up,
            InputKey.Down);
        if (direction == Vector2D.Zero)
            direction = InputAxis2D();

        if (direction != Vector2D.Zero)
            MoveBy(direction.Normalize() * (MoveSpeed * dt));

        Position = new Vector2D(
            Math.Clamp(Position.X, HalfSize, _worldWidth - HalfSize),
            Math.Clamp(Position.Y, HalfSize, _worldHeight - HalfSize));

        _fireCooldown = MathF.Max(0f, _fireCooldown - dt);
        if (KeyDown(InputKey.Space) && _fireCooldown <= 0f)
        {
            Spawn(new PlayerBullet(
                _bulletSprite,
                Position + new Vector2D(0f, -HalfSize)));
            _fireCooldown = FireInterval;
        }
    }
}
