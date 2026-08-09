namespace MyGameTemplate;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Player : GameInstance
{
    private const float MoveSpeed = 180f;
    private readonly SpriteRef _projectileSprite;

    public Player(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        _projectileSprite = sprite;
        Color = new(0.25f, 0.75f, 1f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(InputAxis2D() * (MoveSpeed * (float)deltaTime));
        RotateBy((float)deltaTime);

        if (KeyPressed(GameEngine.Core.Domain.Input.InputKey.Space))
            Spawn(new Bullet(_projectileSprite, Position));
    }
}
