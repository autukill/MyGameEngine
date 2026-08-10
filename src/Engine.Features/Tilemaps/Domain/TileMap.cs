namespace GameEngine.Features.Tilemaps.Domain;

using System.Numerics;
using System.Collections.ObjectModel;

public sealed class TileMap
{
    private readonly List<TileLayer> _layers = [];
    private readonly ReadOnlyCollection<TileLayer> _layersView;
    private readonly Dictionary<string, TileLayer> _layersByName = new(StringComparer.Ordinal);

    public TileMap(string name, int chunkWidth = 32, int chunkHeight = 32)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TileMap name cannot be empty.", nameof(name));
        if (chunkWidth is <= 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(chunkWidth));
        if (chunkHeight is <= 0 or > 256)
            throw new ArgumentOutOfRangeException(nameof(chunkHeight));
        Name = name;
        ChunkWidth = chunkWidth;
        ChunkHeight = chunkHeight;
        _layersView = _layers.AsReadOnly();
    }

    public string Name { get; }
    public TileMapRef Ref => new(Name);
    public int ChunkWidth { get; }
    public int ChunkHeight { get; }
    public IReadOnlyList<TileLayer> Layers => _layersView;

    public TileLayer AddLayer(
        string name,
        TileSetRef tileSet,
        int depth = 0,
        Vector2 offset = default,
        bool visible = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tile layer name cannot be empty.", nameof(name));
        if (tileSet.IsEmpty)
            throw new ArgumentException("Tile layer TileSet cannot be empty.", nameof(tileSet));
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (_layersByName.ContainsKey(name))
            throw new ArgumentException($"Tile layer '{name}' already exists.", nameof(name));

        var layer = new TileLayer(name, tileSet, ChunkWidth, ChunkHeight, depth, offset, visible);
        int index = 0;
        while (index < _layers.Count && _layers[index].Depth <= depth) index++;
        _layers.Insert(index, layer);
        _layersByName.Add(name, layer);
        return layer;
    }

    public bool TryGetLayer(string name, out TileLayer layer) =>
        _layersByName.TryGetValue(name, out layer!);

    public TileLayer GetLayer(string name) => TryGetLayer(name, out TileLayer layer)
        ? layer
        : throw new KeyNotFoundException($"Tile layer '{name}' does not exist.");

}

public sealed class TileLayer
{
    private readonly SortedDictionary<TileChunkCoordinate, TileChunk> _chunks = [];
    private readonly List<TileChunkCoordinate> _chunkOrder = [];
    private long _revision;
    private Vector2 _offset;

    internal TileLayer(
        string name,
        TileSetRef tileSet,
        int chunkWidth,
        int chunkHeight,
        int depth,
        Vector2 offset,
        bool visible)
    {
        Name = name;
        TileSet = tileSet;
        ChunkWidth = chunkWidth;
        ChunkHeight = chunkHeight;
        Depth = depth;
        _offset = offset;
        Visible = visible;
    }

    public string Name { get; }
    public TileSetRef TileSet { get; }
    public int ChunkWidth { get; }
    public int ChunkHeight { get; }
    public int Depth { get; }
    public Vector2 Offset
    {
        get => _offset;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (_offset == value) return;
            _offset = value;
            _revision++;
        }
    }
    public bool Visible { get; set; }
    public int AllocatedChunkCount => _chunks.Count;
    public long Revision => _revision;

    public TileCell GetCell(int x, int y)
    {
        Locate(x, y, out TileChunkCoordinate coordinate, out int localX, out int localY);
        return _chunks.TryGetValue(coordinate, out TileChunk? chunk)
            ? chunk.Get(localX, localY)
            : TileCell.Empty;
    }

    public void SetCell(int x, int y, TileCell value)
    {
        ValidateTransform(value.Transform);
        Locate(x, y, out TileChunkCoordinate coordinate, out int localX, out int localY);
        if (!_chunks.TryGetValue(coordinate, out TileChunk? chunk))
        {
            if (value.IsEmpty) return;
            chunk = new TileChunk(ChunkWidth, ChunkHeight);
            _chunks.Add(coordinate, chunk);
            int orderIndex = _chunkOrder.BinarySearch(coordinate);
            _chunkOrder.Insert(~orderIndex, coordinate);
        }
        if (!chunk.Set(localX, localY, value)) return;
        _revision++;
        if (chunk.NonEmptyCount == 0)
        {
            _chunks.Remove(coordinate);
            int orderIndex = _chunkOrder.BinarySearch(coordinate);
            if (orderIndex >= 0) _chunkOrder.RemoveAt(orderIndex);
        }
    }

    public void ClearCell(int x, int y) => SetCell(x, y, TileCell.Empty);

    public bool TryGetChunk(TileChunkCoordinate coordinate, out TileChunk chunk) =>
        _chunks.TryGetValue(coordinate, out chunk!);

    internal KeyValuePair<TileChunkCoordinate, TileChunk> GetAllocatedChunk(int index)
    {
        TileChunkCoordinate coordinate = _chunkOrder[index];
        return new KeyValuePair<TileChunkCoordinate, TileChunk>(coordinate, _chunks[coordinate]);
    }

    private void Locate(
        int x,
        int y,
        out TileChunkCoordinate coordinate,
        out int localX,
        out int localY)
    {
        int chunkX = FloorDiv(x, ChunkWidth);
        int chunkY = FloorDiv(y, ChunkHeight);
        coordinate = new TileChunkCoordinate(chunkX, chunkY);
        localX = (int)((long)x - (long)chunkX * ChunkWidth);
        localY = (int)((long)y - (long)chunkY * ChunkHeight);
    }

    internal static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static void ValidateTransform(TileTransform transform)
    {
        const TileTransform valid = TileTransform.FlipX | TileTransform.FlipY |
                                    TileTransform.Rotate90 | TileTransform.Rotate180;
        if ((transform & ~valid) != 0)
            throw new ArgumentOutOfRangeException(nameof(transform));
    }
}

public sealed class TileChunk
{
    private readonly TileCell[] _cells;

    internal TileChunk(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new TileCell[checked(width * height)];
    }

    public int Width { get; }
    public int Height { get; }
    public int NonEmptyCount { get; private set; }

    public TileCell Get(int x, int y)
    {
        Validate(x, y);
        return _cells[y * Width + x];
    }

    internal bool Set(int x, int y, TileCell value)
    {
        Validate(x, y);
        int index = y * Width + x;
        TileCell previous = _cells[index];
        if (previous == value) return false;
        if (previous.IsEmpty && !value.IsEmpty) NonEmptyCount++;
        else if (!previous.IsEmpty && value.IsEmpty) NonEmptyCount--;
        _cells[index] = value;
        return true;
    }

    private void Validate(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
    }
}
