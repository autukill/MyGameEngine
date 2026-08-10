namespace GameEngine.Features.Tilemaps.Infrastructure;

using GameEngine.Features.Tilemaps.Domain;

public sealed class TileSetLibrary
{
    private readonly Dictionary<string, TileSet> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public TileSetRef Register(TileSet tileSet)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
        if (!_entries.TryAdd(tileSet.Name, tileSet))
            throw new ArgumentException($"TileSet '{tileSet.Name}' is already registered.", nameof(tileSet));
        return tileSet.Ref;
    }

    public bool TryGet(TileSetRef reference, out TileSet tileSet)
    {
        if (!reference.IsEmpty && _entries.TryGetValue(reference.Name, out TileSet? resolved))
        {
            tileSet = resolved;
            return true;
        }
        tileSet = null!;
        return false;
    }

    public TileSet Get(TileSetRef reference) => TryGet(reference, out TileSet tileSet)
        ? tileSet
        : throw new KeyNotFoundException($"TileSet '{reference}' is not registered.");

    public bool Remove(TileSetRef reference) =>
        !reference.IsEmpty && _entries.Remove(reference.Name);

    public void Clear() => _entries.Clear();
}
