namespace GameEngine.Core.Domain.ValueObjects;

using System.Numerics;

/// <summary>
/// 背景平铺模式。
/// </summary>
public enum BackgroundTileMode : byte
{
    /// <summary>无背景精灵，仅清屏色</summary>
    None = 0,
    /// <summary>拉伸填满 Viewport</summary>
    Stretch = 1,
    /// <summary>重复平铺</summary>
    Tile = 2,
}

/// <summary>
/// 场景背景配置值对象。
///
/// 描述 Scene 的背景：清屏颜色、可选背景精灵、平铺模式。
/// 由 SceneRenderPass 在 Begin 之前消费，执行 glClearColor + 可选 Draw 背景精灵。
///
/// GMS 对照：Room 的 Background 属性（background_color, background_index, background_htiled/vtiled）。
/// </summary>
public readonly record struct BackgroundConfig(
    Vector4 ClearColor,
    SpriteRef BackgroundSprite,
    BackgroundTileMode TileMode)
{
    /// <summary>默认纯黑背景</summary>
    public static BackgroundConfig Black => new(
        new Vector4(0f, 0f, 0f, 1f),
        SpriteRef.Empty,
        BackgroundTileMode.None);

    /// <summary>深灰引擎默认背景</summary>
    public static BackgroundConfig EngineDefault => new(
        new Vector4(0.1f, 0.12f, 0.15f, 1.0f),
        SpriteRef.Empty,
        BackgroundTileMode.None);

    /// <summary>纯色背景快捷构造</summary>
    public static BackgroundConfig FromColor(Vector4 clearColor) => new(
        clearColor, SpriteRef.Empty, BackgroundTileMode.None);

    /// <summary>平铺背景快捷构造</summary>
    public static BackgroundConfig Tiled(SpriteRef sprite) => new(
        new Vector4(0f, 0f, 0f, 1f), sprite, BackgroundTileMode.Tile);

    public bool HasSprite => !BackgroundSprite.IsEmpty
                             && TileMode != BackgroundTileMode.None;

    public override string ToString() =>
        HasSprite
            ? $"Bg[color={ClearColor}, sprite={BackgroundSprite}, mode={TileMode}]"
            : $"Bg[color={ClearColor}]";
}
