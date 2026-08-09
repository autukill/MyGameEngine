namespace GameEngine.Core.Domain.Gameplay;

using System.Collections;
using GameEngine.Core.Domain.Entities;

/// <summary>
/// Caller-owned reusable storage for multi-result gameplay queries. Query methods clear the
/// contents before filling it while retaining capacity for later frames.
/// </summary>
public sealed class GameplayQueryBuffer<T> : IReadOnlyList<T> where T : GameInstance
{
    private readonly List<T> _items;

    public GameplayQueryBuffer(int initialCapacity = 0)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _items = new List<T>(initialCapacity);
    }

    public int Count => _items.Count;
    public int Capacity => _items.Capacity;
    public T this[int index] => _items[index];

    /// <summary>Clears results but retains the allocated backing storage.</summary>
    public void Clear() => _items.Clear();

    public int EnsureCapacity(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        return _items.EnsureCapacity(capacity);
    }

    /// <summary>Concrete foreach uses List's struct enumerator without allocation.</summary>
    public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    internal void Add(T item) => _items.Add(item);
}
