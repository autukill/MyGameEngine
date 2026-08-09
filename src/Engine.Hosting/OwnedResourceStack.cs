namespace GameEngine.Hosting;

/// <summary>按创建顺序登记、按逆序幂等释放，用于初始化失败回滚。</summary>
internal sealed class OwnedResourceStack : IDisposable
{
    private List<IDisposable>? _resources = new();

    public T Add<T>(T resource) where T : IDisposable
    {
        ObjectDisposedException.ThrowIf(_resources is null, this);
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        var resources = Interlocked.Exchange(ref _resources, null);
        if (resources is null) return;
        List<Exception>? errors = null;
        for (int i = resources.Count - 1; i >= 0; i--)
        {
            try
            {
                resources[i].Dispose();
            }
            catch (Exception exception)
            {
                (errors ??= new List<Exception>()).Add(exception);
            }
        }
        if (errors is not null) throw new AggregateException(errors);
    }
}
