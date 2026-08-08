namespace GameEngine.Core.Domain.Graphics;

/// <summary>
/// 实例级渲染状态（GMS 的 image_blend + gpu_set_blendmode + depth 的升级版）。
///
/// 值对象：不可变、零 GC、可作为状态指纹判等。
/// 由 SceneAggregate.DrawActive 在调用实例 OnDraw 前应用；
/// SpriteBatch 检测到状态变更时自动 Flush + Apply（文档核心原则）。
///
/// 注意：readonly record struct 的 new() 等价 default（全零），
/// 不应用主构造函数默认参数，因此 Default 必须显式传参。
/// </summary>
public readonly record struct RenderStyle(
    BlendMode BlendMode,
    bool DepthTest = false,
    bool DepthWrite = false)
{
    /// <summary>引擎默认：标准 Alpha 混合 + 关闭深度测试</summary>
    public static RenderStyle Default => new(BlendMode.AlphaBlend, false, false);

    /// <summary>不透明渲染（禁用混合）</summary>
    public static RenderStyle Opaque => new(BlendMode.Opaque, false, false);

    /// <summary>叠加混合（火焰/激光/高光）</summary>
    public static RenderStyle Additive => new(BlendMode.Additive, false, false);
}
