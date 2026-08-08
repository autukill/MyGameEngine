namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>零分配 Sprite 绘制命令。</summary>
public readonly record struct SpriteDrawCommand(
    SpriteRef Sprite,
    float SubImage,
    Vector2 Position,
    Vector2 Scale,
    float RotationRadians,
    Vector4 Color,
    Vector2? SizeOverride = null,
    Vector2? OriginOverride = null);
