namespace MyGameTemplate;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Player : GameInstance
{
    public static readonly PrefabRef<Bullet> BulletPrefab = new("player.bullet");
    private const float MoveSpeed = 180f;
    private readonly GameplayCooldown _fireCooldown = new(0.15d);

    public Player(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Color = new(0.25f, 0.75f, 1f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(InputAxis2D(GameInputs.Move) * (MoveSpeed * (float)deltaTime));
        RotateBy((float)deltaTime);

        _fireCooldown.Update(deltaTime);
        if (ActionDown(GameInputs.Fire) && _fireCooldown.TryUse())
            Spawn(BulletPrefab, Position);
    }
}
