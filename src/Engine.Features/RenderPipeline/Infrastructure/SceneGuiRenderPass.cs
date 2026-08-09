namespace GameEngine.Features.RenderPipeline.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Features.RenderPipeline.Domain;
using Silk.NET.OpenGL;

/// <summary>把 DrawGUI 生命周期绘制到透明 RGBA8/Display Surface。</summary>
public sealed class SceneGuiRenderPass : RenderPass
{
    private readonly GL _gl;
    private readonly SceneAggregate _scene;
    private readonly RenderTarget2D _target;

    public override RenderTarget2D Output => _target;
    public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

    public SceneGuiRenderPass(
        string name,
        GL gl,
        SceneAggregate scene,
        RenderTarget2D target) : base(name)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        if (target.ColorFormat != RenderTargetColorFormat.Rgba8)
            throw new ArgumentException("Scene GUI target must use RGBA8.", nameof(target));
    }

    public override void Execute(in RenderPassContext context)
    {
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        BlendState.AlphaBlend.Apply(_gl);
        DepthStencilState.None.Apply(_gl);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            0, context.ScreenWidth, context.ScreenHeight, 0, -1, 1);
        context.Batch.ShaderResolver?.SetProjection(projection);
        context.DefaultShader.Use();
        context.DefaultShader.SetProjection(projection);
        context.Batch.Begin();
        _scene.DrawGUI(context.Batch);
        context.Batch.End();
    }
}
