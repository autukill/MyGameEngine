using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

namespace MyGame.Runner;

/// <summary>
/// 满屏背景精灵（GameInstance 子类）。
/// 相当于 GMS 的 obj_background，Depth = Background。
/// </summary>
public sealed class BackgroundSprite : GameInstance
{
    private readonly uint _texture;
    private readonly Vector2 _size;
    private readonly Vector4 _color;

    public BackgroundSprite(uint texture, Vector2 size, Vector4 color)
        : base(
            objectTypeName: nameof(BackgroundSprite),
            position: Vector2D.Zero,
            depth: LayerDepth.Background)
    {
        _texture = texture;
        _size = size;
        _color = color;
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        batch.Draw(_texture, Vector2.Zero, _size, _color, new Vector4(0, 0, 1, 1));
    }
}