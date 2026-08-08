namespace MyGame.Runner;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 圆周运动精灵（GameInstance 子类）—— Demo 业务逻辑封装于此。
///
/// GMS 对照：相当于 GMS 中的 obj_orbiting_sprite，有 Create + Step + Draw 事件：
///   - 构造函数 = Create event（设置中心点、半径、相位、颜色、Sprite）
///   - OnStep  = Step event（计算圆周位置，更新 Transform）
///   - OnDraw  = Draw event（默认实现画 Sprite + 叠加颜色）
/// </summary>
public sealed class OrbitingSprite : GameInstance
{
    private readonly Vector2D _center;
    private readonly float _radius;
    private readonly float _phase;
    private float _animTime;

    /// <summary>
    /// 创建一个绕中心点做圆周运动的精灵。
    /// </summary>
    /// <param name="center">圆心（屏幕坐标）</param>
    /// <param name="radius">圆周半径</param>
    /// <param name="phase">初相（决定起始角度）</param>
    /// <param name="color">叠加颜色</param>
    /// <param name="sprite">逻辑 Sprite 引用（原点由 SpriteLibrary 定义）</param>
    public OrbitingSprite(
        Vector2D center,
        float radius,
        float phase,
        Vector4 color,
        SpriteRef sprite)
        : base(
            objectTypeName: nameof(OrbitingSprite),
            position: center,
            depth: LayerDepth.Instances)
    {
        _center = center;
        _radius = radius;
        _phase = phase;
        Color = color;
        Sprite = sprite;
    }

    /// <summary>GMS Step event: 圆周运动</summary>
    public override void OnStep(double deltaTime)
    {
        _animTime += (float)deltaTime;
        var pos = new Vector2D(
            _center.X + MathF.Cos(_animTime + _phase) * _radius,
            _center.Y + MathF.Sin(_animTime + _phase) * _radius);
        Transform = Transform with { Position = pos };
    }

    // Draw 使用 GameInstance 默认 DrawSelf：原点、缩放、旋转、颜色均由统一 Sprite API 处理。
}

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
