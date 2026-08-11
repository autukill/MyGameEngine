namespace GameEngine.Features.ViewportNavigation;

public abstract class ViewportPlugin
{
    private ViewportController? _owner;

    public string Key { get; }
    public int Order { get; }
    public bool IsPaused { get; private set; }
    protected ViewportController Controller => _owner ??
        throw new InvalidOperationException("Viewport plugin is not attached to a controller.");

    protected ViewportPlugin(string key, int order)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Viewport plugin key cannot be empty.", nameof(key));
        Key = key;
        Order = order;
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    public void Reset()
    {
        if (_owner is not null) OnReset(_owner);
    }

    internal void Attach(ViewportController owner)
    {
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("A Viewport plugin cannot be shared by controllers.");
        _owner = owner;
        OnAttached(owner);
    }

    internal void Detach()
    {
        if (_owner is null) return;
        OnDetached(_owner);
        _owner = null;
    }

    internal void Tick(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (!IsPaused) OnUpdate(controller, in input, deltaTime);
    }

    internal void Resize(ViewportController controller)
    {
        if (!IsPaused) OnResize(controller);
    }

    protected virtual void OnAttached(ViewportController controller) { }
    protected virtual void OnDetached(ViewportController controller) { }
    protected virtual void OnReset(ViewportController controller) { }
    protected virtual void OnResize(ViewportController controller) { }
    protected abstract void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime);
}

public sealed class ViewportPluginManager
{
    private readonly ViewportController _owner;
    private readonly List<ViewportPlugin> _plugins = [];
    private readonly Dictionary<string, ViewportPlugin> _byKey = new(StringComparer.Ordinal);

    internal ViewportPluginManager(ViewportController owner) => _owner = owner;

    public int Count => _plugins.Count;

    public void Add(ViewportPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (_byKey.Remove(plugin.Key, out ViewportPlugin? existing))
        {
            _plugins.Remove(existing);
            existing.Detach();
        }
        plugin.Attach(_owner);
        _byKey.Add(plugin.Key, plugin);
        int index = _plugins.BinarySearch(plugin, PluginOrderComparer.Instance);
        if (index < 0) index = ~index;
        _plugins.Insert(index, plugin);
    }

    public bool Remove(string key)
    {
        if (!_byKey.Remove(key, out ViewportPlugin? plugin)) return false;
        _plugins.Remove(plugin);
        plugin.Detach();
        return true;
    }

    public void RemoveAll()
    {
        for (int i = 0; i < _plugins.Count; i++) _plugins[i].Detach();
        _plugins.Clear();
        _byKey.Clear();
    }

    public bool Pause(string key)
    {
        if (!_byKey.TryGetValue(key, out ViewportPlugin? plugin)) return false;
        plugin.Pause();
        return true;
    }

    public bool Resume(string key)
    {
        if (!_byKey.TryGetValue(key, out ViewportPlugin? plugin)) return false;
        plugin.Resume();
        return true;
    }

    public T? Get<T>(string key) where T : ViewportPlugin =>
        _byKey.TryGetValue(key, out ViewportPlugin? plugin) ? plugin as T : null;

    public void Reset()
    {
        for (int i = 0; i < _plugins.Count; i++) _plugins[i].Reset();
    }

    internal void Update(in ViewportInputFrame input, double deltaTime)
    {
        for (int i = 0; i < _plugins.Count; i++)
            _plugins[i].Tick(_owner, in input, deltaTime);
    }

    internal void Resize()
    {
        for (int i = 0; i < _plugins.Count; i++) _plugins[i].Resize(_owner);
    }

    private sealed class PluginOrderComparer : IComparer<ViewportPlugin>
    {
        public static PluginOrderComparer Instance { get; } = new();

        public int Compare(ViewportPlugin? x, ViewportPlugin? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            int order = x.Order.CompareTo(y.Order);
            return order != 0 ? order : string.CompareOrdinal(x.Key, y.Key);
        }
    }
}
