namespace GameEngine.Core.Domain.Graphics;

/// <summary>
/// 混合模式（对应 GMS 的 gpu_set_blendmode）。
/// Core 自有枚举，不依赖 Silk.NET，供 ISpriteBatch 状态控制与 RenderStyle 使用。
/// </summary>
public enum BlendMode
{
    /// <summary>不透明：覆盖目标像素（GMS: bm_normal 但禁用混合）</summary>
    Opaque = 0,

    /// <summary>标准 Alpha 混合（引擎默认）：SrcAlpha, OneMinusSrcAlpha</summary>
    AlphaBlend = 1,

    /// <summary>叠加模式：火焰 / 激光 / 高光（SrcAlpha, One）</summary>
    Additive = 2,
}
