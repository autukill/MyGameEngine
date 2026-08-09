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

    public bool ClearBeforeDraw { get; set; }

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
        int order = 0,
        ViewportFitMode fit = ViewportFitMode.Stretch)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        var handle = new CompositeSourceHandle(++_nextSourceHandle);
        _sources.Add(new CompositeSource(handle, source, rect, blend ?? BlendState.Opaque, order, fit));
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
        if (ClearBeforeDraw)
        {
            _gl.ClearColor(0f, 0f, 0f, 1f);
            _gl.Clear((uint)Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit);
        }
        _blitShader.Use();
        _blitShader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, ctx.ScreenWidth, ctx.ScreenHeight, 0, -1, 1));

        _batch.Begin();
        foreach (var entry in _sources)
        {
            _batch.Flush();
            entry.Blend.Apply(_gl);
            ViewportPlacement placement = ViewportPlacement.Calculate(
                entry.Source.Width,
                entry.Source.Height,
                ctx.ScreenWidth,
                ctx.ScreenHeight,
                entry.Rect,
                entry.Fit);
            _batch.Draw(
                textureHandle: entry.Source.ColorTexture,
                position: new Vector2(placement.X, placement.Y),
                size: new Vector2(placement.Width, placement.Height),
                color: Vector4.One,
                uvBounds: placement.ToTextureUvBounds());
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
        int Order,
        ViewportFitMode Fit);
}

public readonly record struct CompositeSourceHandle(long Value);
