namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// 多 Camera Viewport 合成 Pass：把多个 RenderTarget 按 ViewportRect 绘制到屏幕。
/// 用于分屏 / 小地图 / 反射镜。
/// </summary>
public sealed class ViewportCompositorPass : RenderPass
{
    private readonly GL _gl;
    private readonly IShader _blitShader;
    private readonly SpriteBatch _batch;
    private readonly List<(RenderTarget2D source, ViewportRect rect, BlendState blend)> _sources = new();

    public override RenderTarget2D? Output => null; // 直写屏幕
    public override IEnumerable<RenderTarget2D> Inputs => _sources.Select(s => s.source);

    public ViewportCompositorPass(string name, GL gl, IShader blitShader, SpriteBatch batch)
        : base(name)
    {
        _gl = gl;
        _blitShader = blitShader;
        _batch = batch;
    }

    public void AddSource(RenderTarget2D source, ViewportRect rect,
        BlendState? blend = null)
    {
        _sources.Add((source, rect, blend ?? BlendState.Opaque));
    }

    public void ClearSources() => _sources.Clear();

    public override void Execute(in RenderPassContext ctx)
    {
        _blitShader.Use();
        _blitShader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, ctx.ScreenWidth, ctx.ScreenHeight, 0, -1, 1));

        _batch.Begin();
        foreach (var (source, rect, blend) in _sources)
        {
            blend.Apply(_gl);
            var (x, y, w, h) = rect.ToPixels(ctx.ScreenWidth, ctx.ScreenHeight);
            _batch.Draw(
                textureHandle: source.ColorTexture,
                position: new Vector2(x, y),
                size: new Vector2(w, h),
                color: Vector4.One,
                uvBounds: new Vector4(0, 1, 1, 0) // Y 翻转
            );
        }
        _batch.End();

        // 重置混合状态
        BlendState.AlphaBlend.Apply(_gl);
    }
}
