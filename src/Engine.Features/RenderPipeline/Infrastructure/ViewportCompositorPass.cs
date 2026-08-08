namespace GameEngine.Features.RenderPipeline.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>将多个 RenderTarget 按 Viewport 与混合状态合成到屏幕。</summary>
public sealed class ViewportCompositorPass : RenderPass
{
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly IShader _blitShader;
    private readonly SpriteBatch _batch;
    private readonly List<CompositeSource> _sources = new();
    private long _nextSourceHandle;

    public override RenderTarget2D? Output => null;
    public override IEnumerable<RenderTarget2D> Inputs => _sources.Select(source => source.Source);

    public ViewportCompositorPass(
        string name,
        Silk.NET.OpenGL.GL gl,
        IShader blitShader,
        SpriteBatch batch) : base(name)
    {
        _gl = gl;
        _blitShader = blitShader;
        _batch = batch;
    }

    public CompositeSourceHandle AddSource(
        RenderTarget2D source,
        ViewportRect rect,
        BlendState? blend = null,
        int order = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        var handle = new CompositeSourceHandle(++_nextSourceHandle);
        _sources.Add(new CompositeSource(handle, source, rect, blend ?? BlendState.Opaque, order));
        _sources.Sort(static (left, right) =>
        {
            int byOrder = left.Order.CompareTo(right.Order);
            return byOrder != 0
                ? byOrder
                : left.Handle.Value.CompareTo(right.Handle.Value);
        });
        return handle;
    }

    public bool RemoveSource(CompositeSourceHandle handle)
    {
        int index = _sources.FindIndex(source => source.Handle == handle);
        if (index < 0) return false;
        _sources.RemoveAt(index);
        return true;
    }

    public void ClearSources() => _sources.Clear();

    public override void Execute(in RenderPassContext ctx)
    {
        _blitShader.Use();
        _blitShader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, ctx.ScreenWidth, ctx.ScreenHeight, 0, -1, 1));

        _batch.Begin();
        foreach (var entry in _sources)
        {
            _batch.Flush();
            entry.Blend.Apply(_gl);
            var (x, y, width, height) = entry.Rect.ToPixels(ctx.ScreenWidth, ctx.ScreenHeight);
            _batch.Draw(
                textureHandle: entry.Source.ColorTexture,
                position: new Vector2(x, y),
                size: new Vector2(width, height),
                color: Vector4.One,
                uvBounds: new Vector4(0, 1, 1, 0));
            _batch.Flush();
        }
        _batch.End();
        BlendState.AlphaBlend.Apply(_gl);
    }

    private sealed record CompositeSource(
        CompositeSourceHandle Handle,
        RenderTarget2D Source,
        ViewportRect Rect,
        BlendState Blend,
        int Order);
}

public readonly record struct CompositeSourceHandle(long Value);
