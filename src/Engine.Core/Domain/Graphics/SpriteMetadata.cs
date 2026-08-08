namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;

/// <summary>不含 GPU 状态的 Sprite 元数据。</summary>
public readonly record struct SpriteMetadata(
    Vector2 Size,
    Vector2 Origin,
    int FrameCount,
    float FramesPerSecond);

/// <summary>一次绘制所需的已解析 Sprite 帧。</summary>
public readonly record struct ResolvedSpriteFrame(
    uint TextureHandle,
    Vector2 Size,
    Vector2 Origin,
    Vector4 UvBounds);
