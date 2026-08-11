namespace GameEngine.Features.TileWorlds.Domain;

using System.Numerics;
using GameEngine.Features.Tilemaps.Domain;

public readonly record struct TileWorldRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);
    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct TileWorldChunkKey(int Level, int X, int Y) : IComparable<TileWorldChunkKey>
{
    public int CompareTo(TileWorldChunkKey other)
    {
        int byLevel = Level.CompareTo(other.Level);
        if (byLevel != 0) return byLevel;
        int byY = Y.CompareTo(other.Y);
        return byY != 0 ? byY : X.CompareTo(other.X);
    }

    public override string ToString() => $"L{Level} ({X}, {Y})";
}

public enum TileWorldChunkPayloadKind : byte
{
    AuthoritativeTiles = 0,
    RasterLayers = 1
}

public readonly record struct TileWorldChunkBounds
{
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }

    public TileWorldChunkBounds(int minX, int minY, int maxX, int maxY)
    {
        if (minX > maxX) throw new ArgumentOutOfRangeException(nameof(minX));
        if (minY > maxY) throw new ArgumentOutOfRangeException(nameof(minY));
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public long Count => checked(
        checked((long)MaxX - MinX + 1L) *
        checked((long)MaxY - MinY + 1L));

    public bool Contains(int x, int y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

public sealed record TileWorldLayerMetadata(
    string Name,
    TileSetRef TileSet,
    int Depth,
    Vector2 Offset,
    bool Visible);

public sealed class TileWorldMetadata
{
    private readonly TileWorldLayerMetadata[] _layers;

    public TileWorldMetadata(
        string name,
        int chunkWidth,
        int chunkHeight,
        TileWorldChunkBounds bounds,
        int declaredLodCount,
        IEnumerable<TileWorldLayerMetadata> layers)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TileWorld name cannot be empty.", nameof(name));
        if (chunkWidth is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(chunkWidth));
        if (chunkHeight is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(chunkHeight));
        if (declaredLodCount is <= 0 or > 8) throw new ArgumentOutOfRangeException(nameof(declaredLodCount));
        ArgumentNullException.ThrowIfNull(layers);
        _layers = layers.ToArray();
        if (_layers.Length == 0) throw new ArgumentException("TileWorld requires at least one layer.", nameof(layers));
        if (_layers.Select(layer => layer.Name).Distinct(StringComparer.Ordinal).Count() != _layers.Length)
            throw new ArgumentException("TileWorld layer names must be unique.", nameof(layers));
        foreach (TileWorldLayerMetadata layer in _layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Name) || layer.TileSet.IsEmpty ||
                !float.IsFinite(layer.Offset.X) || !float.IsFinite(layer.Offset.Y))
                throw new ArgumentException("TileWorld layer metadata is invalid.", nameof(layers));
        }
        Name = name;
        ChunkWidth = chunkWidth;
        ChunkHeight = chunkHeight;
        Bounds = bounds;
        DeclaredLodCount = declaredLodCount;
    }

    public string Name { get; }
    public TileWorldRef Ref => new(Name);
    public int ChunkWidth { get; }
    public int ChunkHeight { get; }
    public TileWorldChunkBounds Bounds { get; }
    public int DeclaredLodCount { get; }
    public IReadOnlyList<TileWorldLayerMetadata> Layers => _layers;
}

public readonly record struct TileWorldCollisionRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

public sealed record TileWorldChunkLayerData(
    int LayerIndex,
    TileCell[] Cells,
    TileWorldCollisionRect[] CollisionRects);

public sealed class TileWorldChunkData
{
    private readonly TileWorldChunkLayerData[] _layers;

    public TileWorldChunkData(TileWorldChunkKey key, IEnumerable<TileWorldChunkLayerData> layers)
    {
        if (key.Level < 0) throw new ArgumentOutOfRangeException(nameof(key));
        ArgumentNullException.ThrowIfNull(layers);
        Key = key;
        _layers = layers.OrderBy(layer => layer.LayerIndex).ToArray();
        if (_layers.Select(layer => layer.LayerIndex).Distinct().Count() != _layers.Length)
            throw new ArgumentException("Chunk layer indices must be unique.", nameof(layers));
    }

    public TileWorldChunkKey Key { get; }
    public IReadOnlyList<TileWorldChunkLayerData> Layers => _layers;
}
