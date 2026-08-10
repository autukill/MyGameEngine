namespace GameEngine.Features.Tilemaps.Infrastructure;

using GameEngine.Features.Tilemaps.Domain;

public sealed class TileMapLibrary
{
    private readonly Dictionary<string, TileMap> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public TileMapRef Register(TileMap tileMap)
    {
        ArgumentNullException.ThrowIfNull(tileMap);
        if (!_entries.TryAdd(tileMap.Name, tileMap))
            throw new ArgumentException($"TileMap '{tileMap.Name}' is already registered.", nameof(tileMap));
        return tileMap.Ref;
    }

    public bool TryGet(TileMapRef reference, out TileMap tileMap)
    {
        if (!reference.IsEmpty && _entries.TryGetValue(reference.Name, out TileMap? resolved))
        {
            tileMap = resolved;
            return true;
        }
        tileMap = null!;
        return false;
    }

    public TileMap Get(TileMapRef reference) => TryGet(reference, out TileMap tileMap)
        ? tileMap
        : throw new KeyNotFoundException($"TileMap '{reference}' is not registered.");

    public bool Remove(TileMapRef reference) =>
        !reference.IsEmpty && _entries.Remove(reference.Name);

    public void Clear() => _entries.Clear();
}
