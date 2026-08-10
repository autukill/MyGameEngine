namespace TheGodTheyMade.Simulation.Navigation;

public sealed class NavigationPathBuffer
{
    private GridCell[] _items;

    public int Count { get; private set; }
    public int Capacity => _items.Length;
    public ReadOnlySpan<GridCell> Items => _items.AsSpan(0, Count);

    public NavigationPathBuffer(int initialCapacity = 16)
    {
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _items = new GridCell[initialCapacity];
    }

    public GridCell this[int index] => (uint)index < (uint)Count
        ? _items[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public void EnsureCapacity(int capacity)
    {
        if (capacity <= _items.Length) return;
        Array.Resize(ref _items, Math.Max(capacity, _items.Length * 2));
    }

    internal void Clear() => Count = 0;

    internal void Add(GridCell cell)
    {
        EnsureCapacity(Count + 1);
        _items[Count++] = cell;
    }

    internal void Reverse() => Array.Reverse(_items, 0, Count);
}
