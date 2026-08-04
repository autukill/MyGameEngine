namespace GameEngine.Features.TextureAtlas;

using System.Numerics;

/// <summary>
/// 图集中某个子精灵的元数据 (值对象)
/// </summary>
/// <param name="Name">子图标识名称 (如 "player_idle_0")</param>
/// <param name="UvBounds">归一化 UV 坐标 (u0, v0, u1, v1)</param>
/// <param name="SourceSize">原始图像尺寸 (Width, Height)</param>
public readonly record struct SpriteRegion(
    string Name,
    Vector4 UvBounds,
    Vector2 SourceSize
)
{
    public float Width => SourceSize.X;
    public float Height => SourceSize.Y;
}
