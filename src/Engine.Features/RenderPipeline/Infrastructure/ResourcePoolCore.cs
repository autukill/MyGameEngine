namespace GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>与 GPU 无关的复用/所有权核心，供 RenderTargetPool 使用和无窗口测试。</summary>
internal sealed class ResourcePoolCore<TKey, TResource> : IDisposable
    where TKey : notnull
    where TResource : class
{
    private readonly Func<TKey, TResource> _create;
    private readonly Action<TResource> _destroy;
    private readonly Dictionary<TKey, Stack<TResource>> _available = new();
    private readonly Dictionary<TResource, TKey> _owned = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TResource> _leased = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public int TotalCount => _owned.Count;
    public int LeasedCount => _leased.Count;
    public int AvailableCount => TotalCount - LeasedCount;

    public ResourcePoolCore(Func<TKey, TResource> create, Action<TResource> destroy)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _destroy = destroy ?? throw new ArgumentNullException(nameof(destroy));
    }

    public TResource Rent(TKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TResource resource;
        if (_available.TryGetValue(key, out var free) && free.Count > 0)
            resource = free.Pop();
        else
        {
            resource = _create(key);
            _owned.Add(resource, key);
        }

        if (!_leased.Add(resource))
            throw new InvalidOperationException("Resource is already leased.");
        return resource;
    }

    public void Return(TResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_disposed) return;
        if (!_owned.TryGetValue(resource, out var key))
            throw new ArgumentException("Resource does not belong to this pool.", nameof(resource));
        if (!_leased.Remove(resource))
            throw new InvalidOperationException("Resource is not currently leased.");
        if (!_available.TryGetValue(key, out var free))
            _available.Add(key, free = new Stack<TResource>());
        free.Push(resource);
    }

    public void TrimAvailable(Predicate<TKey> keep)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(keep);
        foreach (var key in _available.Keys.Where(key => !keep(key)).ToArray())
        {
            var free = _available[key];
            while (free.TryPop(out var resource))
            {
                _owned.Remove(resource);
                _destroy(resource);
            }
            _available.Remove(key);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var resource in _owned.Keys.ToArray()) _destroy(resource);
        _available.Clear();
        _leased.Clear();
        _owned.Clear();
    }
}
