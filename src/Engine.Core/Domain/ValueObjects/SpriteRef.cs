namespace GameEngine.Core.Domain.ValueObjects;

using System.Numerics;

/// <summary>
/// 精灵引用值对象（对应 GMS 的 sprite_index + image_xscale/yscale + UV 帧）。
/// 携带绘制一个 Sprite 所需的全部数据：纹理句柄、尺寸、UV 边界。
/// 不包含 GL 调用，由 GameInstance.OnDraw 消费。
/// </summary>
public readonly record struct SpriteRef(
    uint TextureHandle,
    float Width,
    float Height,
    Vector4 UvBounds)
{
    /// <summary>空引用（无 Sprite）</summary>
    public static SpriteRef Empty => new(0, 0, 0, new Vector4(0, 0, 1, 1));

    /// <summary>是否绑定了有效纹理</summary>
    public bool IsEmpty => TextureHandle == 0;

    /// <summary>
    /// 从一张全纹理创建 SpriteRef（UV = 0,0 到 1,1）
    /// </summary>
    public static SpriteRef FromTexture(uint handle, float width, float height) =>
        new(handle, width, height, new Vector4(0, 0, 1, 1));

    /// <summary>
    /// 从图集子区域创建 SpriteRef（指定 UV 边界）
    /// </summary>
    public static SpriteRef FromAtlas(uint handle, float width, float height,
        Vector2 uvMin, Vector2 uvMax) =>
        new(handle, width, height, new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y));
}
