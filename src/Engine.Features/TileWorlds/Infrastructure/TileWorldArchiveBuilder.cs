namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Numerics;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;

public static class TileWorldArchiveBuilder
{
    public static TileWorldArchiveBuild BuildLod0(
        TileMap map,
        TileSetLibrary tileSets,
        TileWorldChunkBounds bounds,
        int declaredLodCount = 1,
        TileWorldRasterSettings? rasterSettings = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileSets);
        if (declaredLodCount is <= 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(declaredLodCount));

        Vector2? commonTileSize = null;
        foreach (TileLayer layer in map.Layers)
        {
            TileSet tileSet = tileSets.Get(layer.TileSet);
            if (commonTileSize is { } known && known != tileSet.TileSize)
                throw new InvalidDataException("TileWorld requires one common Tile size across all layers.");
            commonTileSize = tileSet.TileSize;
        }

        var metadata = new TileWorldMetadata(
            map.Name,
            map.ChunkWidth,
            map.ChunkHeight,
            commonTileSize ?? throw new InvalidDataException("TileWorld requires at least one TileSet."),
            bounds,
            declaredLodCount,
            rasterSettings ?? new TileWorldRasterSettings(
                512, 512, 2, TileWorldRasterSampling.Smooth),
            map.Layers.Select(layer => new TileWorldLayerMetadata(
                layer.Name, layer.TileSet, layer.Depth, layer.Offset, layer.Visible)));
        var coordinates = new SortedSet<TileChunkCoordinate>();
        foreach (TileLayer layer in map.Layers)
        {
            for (int i = 0; i < layer.AllocatedChunkCount; i++)
            {
                TileChunkCoordinate coordinate = layer.GetAllocatedChunk(i).Key;
                if (!bounds.Contains(coordinate.X, coordinate.Y))
                    throw new InvalidDataException(
                        $"TileMap Chunk '{coordinate}' is outside declared TileWorld bounds.");
                coordinates.Add(coordinate);
            }
        }

        var collisionByLayer = new Dictionary<int, Dictionary<TileChunkCoordinate, List<TileWorldCollisionRect>>>();
        var collisionBuffer = new TileCollisionBakeBuffer();
        var collisionBaker = new TileCollisionBaker(tileSets);
        for (int layerIndex = 0; layerIndex < map.Layers.Count; layerIndex++)
        {
            TileLayer layer = map.Layers[layerIndex];
            collisionBaker.BakeLayer(map, layer.Name, collisionBuffer);
            var byChunk = new Dictionary<TileChunkCoordinate, List<TileWorldCollisionRect>>();
            foreach (TileCollisionRect collision in collisionBuffer.Items)
            {
                if (!byChunk.TryGetValue(collision.Chunk, out List<TileWorldCollisionRect>? list))
                {
                    list = [];
                    byChunk.Add(collision.Chunk, list);
                }
                list.Add(new TileWorldCollisionRect(
                    collision.Bounds.Left,
                    collision.Bounds.Top,
                    collision.Bounds.Right,
                    collision.Bounds.Bottom));
            }
            collisionByLayer.Add(layerIndex, byChunk);
        }

        var chunks = new List<TileWorldChunkData>(coordinates.Count);
        foreach (TileChunkCoordinate coordinate in coordinates)
        {
            var chunkLayers = new List<TileWorldChunkLayerData>();
            for (int layerIndex = 0; layerIndex < map.Layers.Count; layerIndex++)
            {
                TileLayer layer = map.Layers[layerIndex];
                if (!layer.TryGetChunk(coordinate, out TileChunk? chunk)) continue;
                var cells = new TileCell[checked(map.ChunkWidth * map.ChunkHeight)];
                for (int y = 0; y < map.ChunkHeight; y++)
                    for (int x = 0; x < map.ChunkWidth; x++)
                        cells[y * map.ChunkWidth + x] = chunk.Get(x, y);
                TileWorldCollisionRect[] collisions = collisionByLayer[layerIndex]
                    .TryGetValue(coordinate, out List<TileWorldCollisionRect>? values)
                    ? values.ToArray()
                    : [];
                chunkLayers.Add(new TileWorldChunkLayerData(layerIndex, cells, collisions));
            }
            chunks.Add(new TileWorldChunkData(
                new TileWorldChunkKey(0, coordinate.X, coordinate.Y), chunkLayers));
        }
        return new TileWorldArchiveBuild(metadata, chunks);
    }
}
