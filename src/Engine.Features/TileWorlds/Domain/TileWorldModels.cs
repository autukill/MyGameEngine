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

public enum TileWorldRasterEncoding : byte
{
    WebpLossless = 1
}

public enum TileWorldRasterSampling : byte
{
    Smooth = 0,
    PixelArt = 1
}

public readonly record struct TileWorldRasterSettings
{
    public TileWorldRasterSettings(
        int width,
        int height,
        int gutter,
        TileWorldRasterSampling sampling)
    {
        if (width is <= 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is <= 0 or > 8192) throw new ArgumentOutOfRangeException(nameof(height));
        if (gutter is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(gutter));
        if (!Enum.IsDefined(sampling)) throw new ArgumentOutOfRangeException(nameof(sampling));
        if ((long)(width + gutter * 2) * (height + gutter * 2) > 67_108_864L)
            throw new ArgumentOutOfRangeException(nameof(width), "Raster Chunk exceeds the pixel limit.");
        Width = width;
        Height = height;
        Gutter = gutter;
        Sampling = sampling;
    }

    public int Width { get; }
    public int Height { get; }
    public int Gutter { get; }
    public TileWorldRasterSampling Sampling { get; }
    public int EncodedWidth => checked(Width + Gutter * 2);
    public int EncodedHeight => checked(Height + Gutter * 2);
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
        Vector2 tileSize,
        TileWorldChunkBounds bounds,
        int declaredLodCount,
        TileWorldRasterSettings rasterSettings,
        IEnumerable<TileWorldLayerMetadata> layers)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("TileWorld name cannot be empty.", nameof(name));
        if (chunkWidth is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(chunkWidth));
        if (chunkHeight is <= 0 or > 256) throw new ArgumentOutOfRangeException(nameof(chunkHeight));
        if (!float.IsFinite(tileSize.X) || !float.IsFinite(tileSize.Y) ||
            tileSize.X <= 0f || tileSize.Y <= 0f)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        if (declaredLodCount is <= 0 or > 8) throw new ArgumentOutOfRangeException(nameof(declaredLodCount));
        rasterSettings = new TileWorldRasterSettings(
            rasterSettings.Width,
            rasterSettings.Height,
            rasterSettings.Gutter,
            rasterSettings.Sampling);
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
        TileSize = tileSize;
        Bounds = bounds;
        DeclaredLodCount = declaredLodCount;
        RasterSettings = rasterSettings;
    }

    public string Name { get; }
    public TileWorldRef Ref => new(Name);
    public int ChunkWidth { get; }
    public int ChunkHeight { get; }
    public Vector2 TileSize { get; }
    public TileWorldChunkBounds Bounds { get; }
    public int DeclaredLodCount { get; }
    public TileWorldRasterSettings RasterSettings { get; }
    public IReadOnlyList<TileWorldLayerMetadata> Layers => _layers;

    public Vector2 BaseChunkWorldSize => new(ChunkWidth * TileSize.X, ChunkHeight * TileSize.Y);

    public TileWorldChunkBounds GetChunkBounds(int level)
    {
        if ((uint)level >= (uint)DeclaredLodCount)
            throw new ArgumentOutOfRangeException(nameof(level));
        int factor = 1 << level;
        return new TileWorldChunkBounds(
            FloorDiv(Bounds.MinX, factor),
            FloorDiv(Bounds.MinY, factor),
            FloorDiv(Bounds.MaxX, factor),
            FloorDiv(Bounds.MaxY, factor));
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }
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

public sealed record TileWorldRasterLayerData
{
    public TileWorldRasterLayerData(
        int layerIndex,
        int width,
        int height,
        int gutter,
        TileWorldRasterEncoding encoding,
        byte[] encodedBytes)
    {
        if (layerIndex < 0) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (gutter is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(gutter));
        if (!Enum.IsDefined(encoding)) throw new ArgumentOutOfRangeException(nameof(encoding));
        ArgumentNullException.ThrowIfNull(encodedBytes);
        if (encodedBytes.Length == 0) throw new ArgumentException("Encoded raster data cannot be empty.", nameof(encodedBytes));
        LayerIndex = layerIndex;
        Width = width;
        Height = height;
        Gutter = gutter;
        Encoding = encoding;
        EncodedBytes = encodedBytes;
    }

    public int LayerIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public int Gutter { get; }
    public TileWorldRasterEncoding Encoding { get; }
    public byte[] EncodedBytes { get; }
    public int EncodedWidth => checked(Width + Gutter * 2);
    public int EncodedHeight => checked(Height + Gutter * 2);
}

public sealed class TileWorldRasterChunkData
{
    private readonly TileWorldRasterLayerData[] _layers;

    public TileWorldRasterChunkData(
        TileWorldChunkKey key,
        IEnumerable<TileWorldRasterLayerData> layers)
    {
        if (key.Level <= 0) throw new ArgumentOutOfRangeException(nameof(key));
        ArgumentNullException.ThrowIfNull(layers);
        Key = key;
        _layers = layers.OrderBy(layer => layer.LayerIndex).ToArray();
        if (_layers.Length == 0)
            throw new ArgumentException("Raster Chunk requires at least one layer.", nameof(layers));
        if (_layers.Select(layer => layer.LayerIndex).Distinct().Count() != _layers.Length)
            throw new ArgumentException("Raster Chunk layer indices must be unique.", nameof(layers));
    }

    public TileWorldChunkKey Key { get; }
    public IReadOnlyList<TileWorldRasterLayerData> Layers => _layers;
}
