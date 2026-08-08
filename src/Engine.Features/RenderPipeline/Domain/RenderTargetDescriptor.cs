namespace GameEngine.Features.RenderPipeline.Domain;

public enum RenderTargetColorFormat
{
    Rgba8
}

public enum RenderTargetDepthStencilFormat
{
    None,
    Depth24Stencil8
}

/// <summary>RenderTarget 的完整复用键。v1 固定支持 RGBA8 与可选 D24S8。</summary>
public readonly record struct RenderTargetDescriptor
{
    public int Width { get; }
    public int Height { get; }
    public RenderTargetColorFormat ColorFormat { get; }
    public RenderTargetDepthStencilFormat DepthStencilFormat { get; }
    public bool HasDepthStencil =>
        DepthStencilFormat == RenderTargetDepthStencilFormat.Depth24Stencil8;

    public RenderTargetDescriptor(
        int width,
        int height,
        RenderTargetColorFormat colorFormat = RenderTargetColorFormat.Rgba8,
        RenderTargetDepthStencilFormat depthStencilFormat = RenderTargetDepthStencilFormat.None)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (colorFormat != RenderTargetColorFormat.Rgba8)
            throw new ArgumentOutOfRangeException(nameof(colorFormat));
        if (!Enum.IsDefined(depthStencilFormat))
            throw new ArgumentOutOfRangeException(nameof(depthStencilFormat));
        Width = width;
        Height = height;
        ColorFormat = colorFormat;
        DepthStencilFormat = depthStencilFormat;
    }
}
