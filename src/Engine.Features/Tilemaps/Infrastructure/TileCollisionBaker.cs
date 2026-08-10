namespace GameEngine.Features.Tilemaps.Infrastructure;

using System.Numerics;
using System.Runtime.CompilerServices;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.Tilemaps.Domain;

public readonly record struct TileCollisionRect(
    string Layer,
    TileChunkCoordinate Chunk,
    Bounds2D Bounds);

/// <summary>Reusable collision output and scratch storage; repeated bakes allocate only when capacity grows.</summary>
public sealed class TileCollisionBakeBuffer(int initialCapacity = 16)
{
    private TileCollisionRect[] _items = new TileCollisionRect[Math.Max(1, initialCapacity)];
    private bool[] _visited = [];

    public int Count { get; private set; }
    public ReadOnlySpan<TileCollisionRect> Items => _items.AsSpan(0, Count);
    public TileCollisionRect this[int index] => (uint)index < (uint)Count
        ? _items[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public void Clear() => Count = 0;

    internal Span<bool> RentScratch(int length)
    {
        if (_visited.Length < length)
            Array.Resize(ref _visited, Math.Max(length, _visited.Length * 2));
        Span<bool> result = _visited.AsSpan(0, length);
        result.Clear();
        return result;
    }

    internal void Add(TileCollisionRect item)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        _items[Count++] = item;
    }
}

/// <summary>
/// Greedily merges solid cells inside each Chunk. Chunk-local output keeps incremental rebuilds
/// bounded; rectangles intentionally never span a Chunk boundary.
/// </summary>
public sealed class TileCollisionBaker(TileSetLibrary tileSets)
{
    private readonly TileSetLibrary _tileSets = tileSets ?? throw new ArgumentNullException(nameof(tileSets));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int BakeLayer(
        TileMap map,
        string layerName,
        TileCollisionBakeBuffer output,
        Vector2 worldOrigin = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(output);
        if (!float.IsFinite(worldOrigin.X) || !float.IsFinite(worldOrigin.Y))
            throw new ArgumentOutOfRangeException(nameof(worldOrigin));

        TileLayer layer = map.GetLayer(layerName);
        TileSet tileSet = _tileSets.Get(layer.TileSet);
        output.Clear();
        for (int index = 0; index < layer.AllocatedChunkCount; index++)
        {
            KeyValuePair<TileChunkCoordinate, TileChunk> item = layer.GetAllocatedChunk(index);
            BakeChunk(layer, tileSet, item.Key, item.Value, output, worldOrigin);
        }
        return output.Count;
    }

    private static void BakeChunk(
        TileLayer layer,
        TileSet tileSet,
        TileChunkCoordinate coordinate,
        TileChunk chunk,
        TileCollisionBakeBuffer output,
        Vector2 worldOrigin)
    {
        Span<bool> visited = output.RentScratch(checked(chunk.Width * chunk.Height));
        for (int y = 0; y < chunk.Height; y++)
        {
            for (int x = 0; x < chunk.Width; x++)
            {
                int start = y * chunk.Width + x;
                if (visited[start] || !IsSolid(chunk.Get(x, y), tileSet)) continue;

                int width = 1;
                while (x + width < chunk.Width)
                {
                    int index = y * chunk.Width + x + width;
                    if (visited[index] || !IsSolid(chunk.Get(x + width, y), tileSet)) break;
                    width++;
                }

                int height = 1;
                while (y + height < chunk.Height &&
                       RowIsSolid(chunk, tileSet, visited, x, y + height, width))
                    height++;
                for (int row = 0; row < height; row++)
                    visited.Slice((y + row) * chunk.Width + x, width).Fill(true);

                Vector2 tileSize = tileSet.TileSize;
                Vector2 topLeft = worldOrigin + layer.Offset + new Vector2(
                    ((float)coordinate.X * chunk.Width + x) * tileSize.X,
                    ((float)coordinate.Y * chunk.Height + y) * tileSize.Y);
                output.Add(new TileCollisionRect(
                    layer.Name,
                    coordinate,
                    new Bounds2D(
                        topLeft.X,
                        topLeft.Y,
                        topLeft.X + width * tileSize.X,
                        topLeft.Y + height * tileSize.Y)));

            }
        }
    }

    private static bool RowIsSolid(
        TileChunk chunk,
        TileSet tileSet,
        Span<bool> visited,
        int x,
        int row,
        int width)
    {
        for (int column = 0; column < width; column++)
        {
            int index = row * chunk.Width + x + column;
            if (visited[index] || !IsSolid(chunk.Get(x + column, row), tileSet))
                return false;
        }
        return true;
    }

    private static bool IsSolid(TileCell cell, TileSet tileSet) =>
        !cell.IsEmpty && tileSet.TryGet(cell.Tile, out TileDefinition definition) &&
        definition.Collision == TileCollisionKind.Solid;
}
