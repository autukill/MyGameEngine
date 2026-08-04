namespace GameEngine.Features.StencilMasking;

/// <summary>
/// 遮罩模式定义
/// </summary>
public enum StencilMaskMode {
    /// <summary> 显示遮罩内部 (Inverted = false) </summary>
    /// 聚光灯照射区、圆形/矩形 UI ScrollView 裁剪框、小地图视口
    ShowInside,

    /// <summary> 显示遮罩外部/反向遮罩 (Inverted = true) </summary>
    /// 战争迷雾挖孔、墙体挡住角色时的透视洞、黑洞吞噬效果
    ShowOutside
}

public readonly record struct StencilMaskState(
    uint StencilRef = 1,
    uint MaskBits = 0xFF,
    StencilMaskMode Mode = StencilMaskMode.ShowInside
);