namespace GameEngine.Features.Tilemaps.Infrastructure;

using System.Numerics;
using System.Runtime.CompilerServices;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.Tilemaps.Domain;

public readonly record struct TileMapDrawStatistics(
    int VisitedChunks,
    int MissingChunks,
    int VisitedCells,
    int DrawnTiles,
    int UnknownTiles);

/// <summary>
/// Allocation-free visible-region renderer. Visibility is explicit so one TileMap can be drawn
/// by several Cameras or Viewports without carrying a hidden global Camera.
/// </summary>
public sealed class TileMapRenderer(TileSetLibrary tileSets)
{
    private readonly TileSetLibrary _tileSets = tileSets ?? throw new ArgumentNullException(nameof(tileSets));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public TileMapDrawStatistics Draw(
        ISpriteBatch batch,
        TileMap map,
        Bounds2D visibleWorldBounds,
        Vector2 worldOrigin = default,
        Vector4? color = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(map);
        if (!float.IsFinite(worldOrigin.X) || !float.IsFinite(worldOrigin.Y))
            throw new ArgumentOutOfRangeException(nameof(worldOrigin));

        int visitedChunks = 0;
        int missingChunks = 0;
        int visitedCells = 0;
        int drawnTiles = 0;
        int unknownTiles = 0;
        Vector4 tint = color ?? Vector4.One;

        IReadOnlyList<TileLayer> layers = map.Layers;
        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            TileLayer layer = layers[layerIndex];
            if (!layer.Visible) continue;
            if (!_tileSets.TryGet(layer.TileSet, out TileSet tileSet))
            {
                unknownTiles++;
                continue;
            }

            Vector2 origin = worldOrigin + layer.Offset;
            Vector2 size = tileSet.TileSize;
            int minCellX = FloorToInt((visibleWorldBounds.Left - origin.X) / size.X);
            int minCellY = FloorToInt((visibleWorldBounds.Top - origin.Y) / size.Y);
            int maxCellX = FloorToInt(MathF.BitDecrement(
                (visibleWorldBounds.Right - origin.X) / size.X));
            int maxCellY = FloorToInt(MathF.BitDecrement(
                (visibleWorldBounds.Bottom - origin.Y) / size.Y));
            if (maxCellX < minCellX || maxCellY < minCellY) continue;

            int minChunkX = TileLayer.FloorDiv(minCellX, map.ChunkWidth);
            int minChunkY = TileLayer.FloorDiv(minCellY, map.ChunkHeight);
            int maxChunkX = TileLayer.FloorDiv(maxCellX, map.ChunkWidth);
            int maxChunkY = TileLayer.FloorDiv(maxCellY, map.ChunkHeight);

            for (long chunkYValue = minChunkY; chunkYValue <= maxChunkY; chunkYValue++)
            {
                int chunkY = (int)chunkYValue;
                for (long chunkXValue = minChunkX; chunkXValue <= maxChunkX; chunkXValue++)
                {
                    int chunkX = (int)chunkXValue;
                    var coordinate = new TileChunkCoordinate(chunkX, chunkY);
                    if (!layer.TryGetChunk(coordinate, out TileChunk chunk))
                    {
                        missingChunks++;
                        continue;
                    }
                    visitedChunks++;

                    long chunkCellX = (long)chunkX * map.ChunkWidth;
                    long chunkCellY = (long)chunkY * map.ChunkHeight;
                    int fromX = (int)Math.Max(0L, (long)minCellX - chunkCellX);
                    int fromY = (int)Math.Max(0L, (long)minCellY - chunkCellY);
                    int toX = (int)Math.Min(map.ChunkWidth - 1L, (long)maxCellX - chunkCellX);
                    int toY = (int)Math.Min(map.ChunkHeight - 1L, (long)maxCellY - chunkCellY);
                    for (int localY = fromY; localY <= toY; localY++)
                    {
                        for (int localX = fromX; localX <= toX; localX++)
                        {
                            visitedCells++;
                            TileCell cell = chunk.Get(localX, localY);
                            if (cell.IsEmpty) continue;
                            if (!tileSet.TryGet(cell.Tile, out TileDefinition definition))
                            {
                                unknownTiles++;
                                continue;
                            }

                            Vector2 center = origin + new Vector2(
                                ((float)chunkCellX + localX + 0.5f) * size.X,
                                ((float)chunkCellY + localY + 0.5f) * size.Y);
                            GetTransform(cell.Transform, out Vector2 scale, out float rotation);
                            batch.DrawSpriteCommand(new SpriteDrawCommand(
                                definition.Sprite,
                                definition.SubImage,
                                center,
                                scale,
                                rotation,
                                tint,
                                SizeOverride: size,
                                OriginOverride: size * 0.5f));
                            drawnTiles++;
                        }
                    }
                }
            }
        }

        return new TileMapDrawStatistics(
            visitedChunks,
            missingChunks,
            visitedCells,
            drawnTiles,
            unknownTiles);
    }

    internal static void GetTransform(
        TileTransform transform,
        out Vector2 scale,
        out float rotation)
    {
        scale = new Vector2(
            (transform & TileTransform.FlipX) != 0 ? -1f : 1f,
            (transform & TileTransform.FlipY) != 0 ? -1f : 1f);
        TileTransform rotationBits = transform & (TileTransform.Rotate90 | TileTransform.Rotate180);
        rotation = rotationBits switch
        {
            TileTransform.Rotate90 => MathF.PI * 0.5f,
            TileTransform.Rotate180 => MathF.PI,
            TileTransform.Rotate270 => MathF.PI * 1.5f,
            _ => 0f
        };
    }

    private static int FloorToInt(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Visible Tile range must be finite.");
        value = MathF.Floor(value);
        if (value < int.MinValue || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Visible Tile range exceeds Int32 coordinates.");
        return (int)value;
    }
}
