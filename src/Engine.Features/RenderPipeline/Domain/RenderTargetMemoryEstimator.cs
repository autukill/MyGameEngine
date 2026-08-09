namespace GameEngine.Features.RenderPipeline.Domain;

/// <summary>按声明格式估算 RenderTarget attachment 字节数；不包含驱动对齐和 FBO 元数据。</summary>
public static class RenderTargetMemoryEstimator
{
    public static long EstimateColorBytes(in RenderTargetDescriptor descriptor)
    {
        int bytesPerPixel = descriptor.ColorFormat switch
        {
            RenderTargetColorFormat.Rgba8 => 4,
            RenderTargetColorFormat.Rgba16Float => 8,
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
        };
        return checked((long)descriptor.Width * descriptor.Height * bytesPerPixel);
    }

    public static long EstimateDepthStencilBytes(in RenderTargetDescriptor descriptor) =>
        descriptor.DepthStencilFormat switch
        {
            RenderTargetDepthStencilFormat.None => 0L,
            RenderTargetDepthStencilFormat.Depth24Stencil8 =>
                checked((long)descriptor.Width * descriptor.Height * 4L),
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
        };

    public static long EstimateBytes(in RenderTargetDescriptor descriptor) =>
        checked(EstimateColorBytes(descriptor) + EstimateDepthStencilBytes(descriptor));
}
