namespace GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// 遮罩模式定义。
/// 决定 Stencil Test 使用 EQUAL 还是 NOT_EQUAL，对应"显示遮罩内部/外部"两种语义。
/// </summary>
public enum StencilMaskMode
{
    /// <summary>
    /// 显示遮罩内部 (Inverted = false)。
    /// Stencil Test 使用 EQUAL：只在遮罩几何覆盖过的像素上绘制内容。
    /// 典型应用：聚光灯照射区、圆形/矩形 UI ScrollView 裁剪框、小地图视口。
    /// </summary>
    ShowInside,

    /// <summary>
    /// 显示遮罩外部/反向遮罩 (Inverted = true)。
    /// Stencil Test 使用 NOT_EQUAL：在遮罩几何覆盖过的像素上不绘制内容。
    /// 典型应用：战争迷雾挖孔、墙体挡住角色时的透视洞、黑洞吞噬效果。
    /// </summary>
    ShowOutside
}

/// <summary>
/// Stencil 遮罩配置状态值对象。
/// 不可变、零 GC、可作为字典 Key 做"状态指纹"比较。
/// 由领域层声明（通过 Command），由渲染层（StencilMaskPass）持有并应用。
/// </summary>
public readonly record struct StencilMaskState(
    uint StencilRef = 1,
    uint MaskBits = 0xFF,
    StencilMaskMode Mode = StencilMaskMode.ShowInside
)
{
    /// <summary>默认配置：ShowInside 模式，参考值 1，全 1 掩码</summary>
    public static StencilMaskState Default => new(
        StencilRef: 1, MaskBits: 0xFF, Mode: StencilMaskMode.ShowInside);

    /// <summary>聚光灯/小地图典型配置</summary>
    public static StencilMaskState Spotlight => new(
        StencilRef: 1, MaskBits: 0xFF, Mode: StencilMaskMode.ShowInside);

    /// <summary>战争迷雾/透视洞典型配置</summary>
    public static StencilMaskState FogOfWarHole => new(
        StencilRef: 1, MaskBits: 0xFF, Mode: StencilMaskMode.ShowOutside);

    /// <summary>取反模式（ShowInside ↔ ShowOutside）</summary>
    public StencilMaskState Inverted => this with
    {
        Mode = Mode == StencilMaskMode.ShowInside
            ? StencilMaskMode.ShowOutside
            : StencilMaskMode.ShowInside
    };
}
