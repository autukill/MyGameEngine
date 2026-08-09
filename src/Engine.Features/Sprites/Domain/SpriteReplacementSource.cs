namespace GameEngine.Features.Sprites.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>描述一次 Sprite 资源修订中的规范化像素帧。</summary>
public sealed record SpriteReplacementSource(
    string Name,
    Vector2 LogicalSize,
    Vector2 Origin,
    SpriteFrameSource[] Frames,
    float FramesPerSecond = 0f);
