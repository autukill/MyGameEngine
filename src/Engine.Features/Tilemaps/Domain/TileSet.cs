namespace GameEngine.Features.Tilemaps.Domain;

using System.Numerics;

public sealed class TileSet
{
    private readonly Dictionary<TileId, TileDefinition> _definitions;

    public TileSet(string name, Vector2 tileSize, IEnumerable<TileDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TileSet name cannot be empty.", nameof(name));
        if (!float.IsFinite(tileSize.X) || !float.IsFinite(tileSize.Y) ||
            tileSize.X <= 0f || tileSize.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        ArgumentNullException.ThrowIfNull(definitions);

        Name = name;
        TileSize = tileSize;
        _definitions = new Dictionary<TileId, TileDefinition>();
        foreach (TileDefinition definition in definitions)
        {
            definition.Validate();
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new ArgumentException(
                    $"Tile id '{definition.Id}' appears more than once.", nameof(definitions));
        }
        if (_definitions.Count == 0)
            throw new ArgumentException("A TileSet requires at least one Tile definition.", nameof(definitions));
    }

    public string Name { get; }
    public TileSetRef Ref => new(Name);
    public Vector2 TileSize { get; }
    public int Count => _definitions.Count;

    public bool TryGet(TileId id, out TileDefinition definition) =>
        _definitions.TryGetValue(id, out definition!);
}
