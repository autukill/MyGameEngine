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
    private readonly Dictionary<long, RenderTargetDescriptor> _activeLeases = new();
    private long _nextLeaseId;
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
                descriptor),
            target => target.Dispose());
    }

    public RenderTargetLease Rent(RenderTargetDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = _resources.Rent(descriptor);
        long leaseId = ++_nextLeaseId;
        _activeLeases.Add(leaseId, descriptor);
        return new RenderTargetLease(this, leaseId, descriptor, target);
    }

    internal void Return(long leaseId, RenderTarget2D target)
    {
        if (_disposed) return;
        _resources.Return(target);
        if (!_activeLeases.Remove(leaseId))
            throw new InvalidOperationException("RenderTarget lease is not active.");
    }

    /// <summary>显式分配一个按 Descriptor 分组并列出活动租约的只读快照。</summary>
    public RenderTargetPoolDiagnostics CaptureDiagnostics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var descriptors = _resources.CaptureDiagnostics()
            .OrderBy(item => item.Key.Width)
            .ThenBy(item => item.Key.Height)
            .ThenBy(item => item.Key.ColorFormat)
            .ThenBy(item => item.Key.DepthStencilFormat)
            .Select(item => new RenderTargetDescriptorDiagnostics(
                item.Key,
                item.TotalCount,
                item.LeasedCount,
                item.AvailableCount));
        var leases = _activeLeases
            .OrderBy(pair => pair.Key)
            .Select(pair => new RenderTargetLeaseDiagnostics(pair.Key, pair.Value));
        return new RenderTargetPoolDiagnostics(
            TotalCount,
            LeasedCount,
            AvailableCount,
            descriptors,
            leases);
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
        _activeLeases.Clear();
        _resources.Dispose();
    }
}

public sealed class RenderTargetLease : IDisposable
{
    private RenderTargetPool? _owner;

    public RenderTargetDescriptor Descriptor { get; }
    public long LeaseId { get; }
    public RenderTarget2D Target { get; }
    public bool IsReturned => _owner is null;

    internal RenderTargetLease(
        RenderTargetPool owner,
        long leaseId,
        RenderTargetDescriptor descriptor,
        RenderTarget2D target)
    {
        _owner = owner;
        LeaseId = leaseId;
        Descriptor = descriptor;
        Target = target;
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Return(LeaseId, Target);
    }
}
