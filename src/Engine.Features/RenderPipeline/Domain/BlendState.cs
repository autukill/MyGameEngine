namespace GameEngine.Features.RenderPipeline.Domain;

using Silk.NET.OpenGL;

/// <summary>
/// 混合状态值对象：零 GC、可直接作为字典 Key 做"状态指纹"
/// </summary>
public readonly record struct BlendState(
    bool EnableBlend,
    BlendingFactor SrcFactor,
    BlendingFactor DstFactor,
    bool WriteR = true,
    bool WriteG = true,
    bool WriteB = true,
    bool WriteA = true)
{
    public static BlendState AlphaBlend => new(
        true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

    /// <summary>叠加模式：火焰、激光、爆炸特效</summary>
    public static BlendState Additive => new(
        true, BlendingFactor.SrcAlpha, BlendingFactor.One);

    /// <summary>不透明渲染</summary>
    public static BlendState Opaque => new(
        false, BlendingFactor.One, BlendingFactor.Zero);

    /// <summary>颜色屏蔽：写入 Stencil 时使用</summary>
    public static BlendState ColorMaskDisabled => new(
        false, BlendingFactor.One, BlendingFactor.Zero,
        WriteR: false, WriteG: false, WriteB: false, WriteA: false);

    public void Apply(GL gl)
    {
        if (EnableBlend)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(SrcFactor, DstFactor);
        }
        else
        {
            gl.Disable(EnableCap.Blend);
        }
        gl.ColorMask(WriteR, WriteG, WriteB, WriteA);
    }
}
