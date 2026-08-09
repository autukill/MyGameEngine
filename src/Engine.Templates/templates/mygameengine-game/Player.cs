namespace MyGameTemplate;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Player : GameInstance
{
    public static readonly PrefabRef<Bullet> BulletPrefab = new("player.bullet");
    private const float MoveSpeed = 180f;

    public Player(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Color = new(0.25f, 0.75f, 1f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(InputAxis2D() * (MoveSpeed * (float)deltaTime));
        RotateBy((float)deltaTime);

        if (KeyPressed(GameEngine.Core.Domain.Input.InputKey.Space))
            Spawn(BulletPrefab, Position);
    }
}
