namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using GameEngine.Features.RenderPipeline.Domain;

public interface IRenderTargetPool : IDisposable
{
    RenderTargetLease Rent(RenderTargetDescriptor descriptor);
    void TrimExceptSize(int width, int height);
}

/// <summary>按完整 Descriptor 复用 RenderTarget；Pool 拥有实际 GPU 生命周期。</summary>
public sealed class RenderTargetPool : IRenderTargetPool
{
    private readonly ResourcePoolCore<RenderTargetDescriptor, RenderTarget2D> _resources;
    private bool _disposed;

    public int TotalCount => _resources.TotalCount;
    public int LeasedCount => _resources.LeasedCount;
    public int AvailableCount => _resources.AvailableCount;

    public RenderTargetPool(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);
        _resources = new ResourcePoolCore<RenderTargetDescriptor, RenderTarget2D>(
            descriptor => new RenderTarget2D(
                gl,
                descriptor.Width,
                descriptor.Height,
                descriptor.HasDepthStencil),
            target => target.Dispose());
    }

    public RenderTargetLease Rent(RenderTargetDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = _resources.Rent(descriptor);
        return new RenderTargetLease(this, descriptor, target);
    }

    internal void Return(RenderTarget2D target)
    {
        if (_disposed) return;
        _resources.Return(target);
    }

    public void TrimExceptSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.TrimAvailable(key => key.Width == width && key.Height == height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resources.Dispose();
    }
}

public sealed class RenderTargetLease : IDisposable
{
    private RenderTargetPool? _owner;

    public RenderTargetDescriptor Descriptor { get; }
    public RenderTarget2D Target { get; }
    public bool IsReturned => _owner is null;

    internal RenderTargetLease(
        RenderTargetPool owner,
        RenderTargetDescriptor descriptor,
        RenderTarget2D target)
    {
        _owner = owner;
        Descriptor = descriptor;
        Target = target;
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(Target);
    }
}
