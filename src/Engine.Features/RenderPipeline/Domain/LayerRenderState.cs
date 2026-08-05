namespace GameEngine.Features.RenderPipeline.Domain;

using GameEngine.Core.Infrastructure.Graphics;

/// <summary>
/// Per-Layer 渲染状态：可单独覆盖 Shader/Blend/Stencil。
/// 不指定时（= null）继承上层 Pass 的默认状态。
/// </summary>
public sealed class LayerRenderState
{
    public SpriteShader? ShaderOverride { get; init; }
    public BlendState? BlendOverride { get; init; }
    public DepthStencilState? DepthStencilOverride { get; init; }

    public static LayerRenderState Default => new();

    public static LayerRenderState AdditiveBlend => new()
    {
        BlendOverride = BlendState.Additive
    };

    public static LayerRenderState UI => new()
    {
        DepthStencilOverride = new DepthStencilState(
            DepthTestEnable: false, DepthWriteEnable: false)
    };
}
