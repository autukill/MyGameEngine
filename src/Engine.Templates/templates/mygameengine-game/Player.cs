namespace MyGameTemplate;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Player : GameInstance
{
    public Player(SpriteRef sprite, Vector2D position)
        : base(nameof(Player), position, LayerDepth.Instances)
    {
        Sprite = sprite;
        Color = new(0.25f, 0.75f, 1f, 1f);
    }

    public override void OnStep(double deltaTime)
    {
        Transform = Transform with
        {
            Rotation = Transform.Rotation + (float)deltaTime
        };
    }
}
