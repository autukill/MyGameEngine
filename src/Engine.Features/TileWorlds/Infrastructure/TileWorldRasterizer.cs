namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Numerics;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;

/// <summary>
/// Deterministically rasterizes read-only visual LODs. LOD0 remains authoritative Tile data;
/// a level N raster Chunk covers 2^N by 2^N LOD0 Chunks.
/// </summary>
public static class TileWorldRasterizer
{
    public static IEnumerable<TileWorldRasterChunkImage> RasterizeLodLevels(
        TileMap map,
        TileSetLibrary tileSets,
        TileWorldMetadata metadata,
        ITileWorldRasterSource sprites)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileSets);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(sprites);
        ValidateInputs(map, tileSets, metadata);

        for (int level = 1; level < metadata.DeclaredLodCount; level++)
        {
            foreach (TileWorldChunkKey key in CollectCandidateKeys(map, tileSets, metadata, level))
            {
                TileWorldRasterChunkImage? chunk = RasterizeChunk(
                    map, tileSets, metadata, sprites, key);
                if (chunk is not null) yield return chunk;
            }
        }
    }

    private static SortedSet<TileWorldChunkKey> CollectCandidateKeys(
        TileMap map,
        TileSetLibrary tileSets,
        TileWorldMetadata metadata,
        int level)
    {
        var keys = new SortedSet<TileWorldChunkKey>();
        TileWorldChunkBounds bounds = metadata.GetChunkBounds(level);
        Vector2 baseWorldSize = metadata.BaseChunkWorldSize;
        int factor = 1 << level;
        double lodWorldWidth = (double)baseWorldSize.X * factor;
        double lodWorldHeight = (double)baseWorldSize.Y * factor;
        foreach (TileLayer layer in map.Layers)
        {
            if (!layer.Visible) continue;
            TileSet tileSet = tileSets.Get(layer.TileSet);
            double expansion = Math.Max(tileSet.TileSize.X, tileSet.TileSize.Y);
            for (int index = 0; index < layer.AllocatedChunkCount; index++)
            {
                TileChunkCoordinate coordinate = layer.GetAllocatedChunk(index).Key;
                double left = coordinate.X * (double)baseWorldSize.X + layer.Offset.X - expansion;
                double top = coordinate.Y * (double)baseWorldSize.Y + layer.Offset.Y - expansion;
                double right = (coordinate.X + 1d) * baseWorldSize.X + layer.Offset.X + expansion;
                double bottom = (coordinate.Y + 1d) * baseWorldSize.Y + layer.Offset.Y + expansion;
                int minX = Math.Max(bounds.MinX, FloorToInt(left / lodWorldWidth));
                int minY = Math.Max(bounds.MinY, FloorToInt(top / lodWorldHeight));
                int maxX = Math.Min(bounds.MaxX, FloorToInt(Math.BitDecrement(right / lodWorldWidth)));
                int maxY = Math.Min(bounds.MaxY, FloorToInt(Math.BitDecrement(bottom / lodWorldHeight)));
                for (long yValue = minY; yValue <= maxY; yValue++)
                    for (long xValue = minX; xValue <= maxX; xValue++)
                        keys.Add(new TileWorldChunkKey(level, (int)xValue, (int)yValue));
            }
        }
        return keys;
    }

    private static TileWorldRasterChunkImage? RasterizeChunk(
        TileMap map,
        TileSetLibrary tileSets,
        TileWorldMetadata metadata,
        ITileWorldRasterSource sprites,
        TileWorldChunkKey key)
    {
        int factor = 1 << key.Level;
        Vector2 baseWorldSize = metadata.BaseChunkWorldSize;
        double worldWidth = (double)baseWorldSize.X * factor;
        double worldHeight = (double)baseWorldSize.Y * factor;
        double worldLeft = (double)key.X * worldWidth;
        double worldTop = (double)key.Y * worldHeight;
        if (!double.IsFinite(worldLeft) || !double.IsFinite(worldTop) ||
            !double.IsFinite(worldWidth) || !double.IsFinite(worldHeight))
            throw new InvalidDataException($"Raster Chunk '{key}' exceeds finite world coordinates.");

        var layers = new List<TileWorldRasterLayerImage>();
        for (int layerIndex = 0; layerIndex < map.Layers.Count; layerIndex++)
        {
            TileLayer layer = map.Layers[layerIndex];
            if (!layer.Visible) continue;
            TileSet tileSet = tileSets.Get(layer.TileSet);
            TileWorldRasterLayerImage? image = RasterizeLayer(
                map,
                layer,
                layerIndex,
                tileSet,
                sprites,
                metadata.RasterSettings,
                worldLeft,
                worldTop,
                worldWidth,
                worldHeight);
            if (image is not null) layers.Add(image);
        }
        return layers.Count == 0 ? null : new TileWorldRasterChunkImage(key, layers);
    }

    private static TileWorldRasterLayerImage? RasterizeLayer(
        TileMap map,
        TileLayer layer,
        int layerIndex,
        TileSet tileSet,
        ITileWorldRasterSource sprites,
        TileWorldRasterSettings settings,
        double worldLeft,
        double worldTop,
        double worldWidth,
        double worldHeight)
    {
        int encodedWidth = settings.EncodedWidth;
        int encodedHeight = settings.EncodedHeight;
        byte[] pixels = new byte[checked(encodedWidth * encodedHeight * 4)];
        double tileWidth = tileSet.TileSize.X;
        double tileHeight = tileSet.TileSize.Y;
        double expansion = Math.Max(tileWidth, tileHeight);
        int minCellX = FloorToInt((worldLeft - layer.Offset.X - expansion) / tileWidth) - 1;
        int minCellY = FloorToInt((worldTop - layer.Offset.Y - expansion) / tileHeight) - 1;
        int maxCellX = FloorToInt((worldLeft + worldWidth - layer.Offset.X + expansion) / tileWidth) + 1;
        int maxCellY = FloorToInt((worldTop + worldHeight - layer.Offset.Y + expansion) / tileHeight) + 1;
        int minChunkX = FloorDiv(minCellX, map.ChunkWidth);
        int minChunkY = FloorDiv(minCellY, map.ChunkHeight);
        int maxChunkX = FloorDiv(maxCellX, map.ChunkWidth);
        int maxChunkY = FloorDiv(maxCellY, map.ChunkHeight);
        bool hasPixels = false;

        for (long chunkYValue = minChunkY; chunkYValue <= maxChunkY; chunkYValue++)
        {
            int chunkY = (int)chunkYValue;
            for (long chunkXValue = minChunkX; chunkXValue <= maxChunkX; chunkXValue++)
            {
                int chunkX = (int)chunkXValue;
                if (!layer.TryGetChunk(new TileChunkCoordinate(chunkX, chunkY), out TileChunk? chunk))
                    continue;
                long chunkCellX = (long)chunkX * map.ChunkWidth;
                long chunkCellY = (long)chunkY * map.ChunkHeight;
                for (int localY = 0; localY < map.ChunkHeight; localY++)
                {
                    long cellY = chunkCellY + localY;
                    if (cellY < minCellY || cellY > maxCellY) continue;
                    for (int localX = 0; localX < map.ChunkWidth; localX++)
                    {
                        long cellX = chunkCellX + localX;
                        if (cellX < minCellX || cellX > maxCellX) continue;
                        TileCell cell = chunk.Get(localX, localY);
                        if (cell.IsEmpty) continue;
                        if (!tileSet.TryGet(cell.Tile, out TileDefinition? definition))
                            throw new InvalidDataException(
                                $"TileSet '{tileSet.Name}' has no definition for Tile '{cell.Tile}'.");
                        if (!sprites.TryResolve(definition.Sprite, definition.SubImage, out TileWorldRasterSourceFrame frame))
                            throw new InvalidDataException(
                                $"Tile '{cell.Tile}' references unavailable Sprite frame '{definition.Sprite}'[{definition.SubImage}].");
                        double centerX = layer.Offset.X + (cellX + 0.5d) * tileWidth;
                        double centerY = layer.Offset.Y + (cellY + 0.5d) * tileHeight;
                        if (DrawTile(
                                pixels,
                                encodedWidth,
                                settings,
                                frame,
                                cell.Transform,
                                centerX,
                                centerY,
                                tileWidth,
                                tileHeight,
                                worldLeft,
                                worldTop,
                                worldWidth,
                                worldHeight))
                            hasPixels = true;
                    }
                }
            }
        }

        if (!hasPixels) return null;
        ExtrudeGutter(pixels, settings.Width, settings.Height, settings.Gutter);
        return new TileWorldRasterLayerImage(
            layerIndex, settings.Width, settings.Height, settings.Gutter, pixels);
    }

    private static bool DrawTile(
        byte[] destination,
        int destinationStridePixels,
        TileWorldRasterSettings settings,
        TileWorldRasterSourceFrame source,
        TileTransform transform,
        double centerX,
        double centerY,
        double tileWidth,
        double tileHeight,
        double worldLeft,
        double worldTop,
        double worldWidth,
        double worldHeight)
    {
        TileTransformOperations.GetScaleAndRotation(transform, out Vector2 scale, out float rotation);
        double cosine = Math.Cos(rotation);
        double sine = Math.Sin(rotation);
        double halfWidth = tileWidth * 0.5d;
        double halfHeight = tileHeight * 0.5d;
        double extentX = Math.Abs(halfWidth * cosine) + Math.Abs(halfHeight * sine);
        double extentY = Math.Abs(halfWidth * sine) + Math.Abs(halfHeight * cosine);
        double stepX = worldWidth / settings.Width;
        double stepY = worldHeight / settings.Height;
        int minX = Math.Clamp((int)Math.Floor((centerX - extentX - worldLeft) / stepX) - 1, 0, settings.Width - 1);
        int maxX = Math.Clamp((int)Math.Ceiling((centerX + extentX - worldLeft) / stepX) + 1, 0, settings.Width - 1);
        int minY = Math.Clamp((int)Math.Floor((centerY - extentY - worldTop) / stepY) - 1, 0, settings.Height - 1);
        int maxY = Math.Clamp((int)Math.Ceiling((centerY + extentY - worldTop) / stepY) + 1, 0, settings.Height - 1);
        if (maxX < minX || maxY < minY) return false;

        bool drew = false;
        for (int y = minY; y <= maxY; y++)
        {
            double worldY = worldTop + (y + 0.5d) * stepY;
            for (int x = minX; x <= maxX; x++)
            {
                double worldX = worldLeft + (x + 0.5d) * stepX;
                double dx = worldX - centerX;
                double dy = worldY - centerY;
                double scaledX = dx * cosine - dy * sine;
                double scaledY = dx * sine + dy * cosine;
                double localX = scaledX / scale.X;
                double localY = scaledY / scale.Y;
                double u = localX / tileWidth + 0.5d;
                double v = localY / tileHeight + 0.5d;
                if (u < 0d || v < 0d || u >= 1d || v >= 1d) continue;
                Sample(source, u, v, settings.Sampling, out byte red, out byte green, out byte blue, out byte alpha);
                if (alpha == 0) continue;
                int destinationIndex = checked(
                    ((y + settings.Gutter) * destinationStridePixels + x + settings.Gutter) * 4);
                BlendSourceOver(destination, destinationIndex, red, green, blue, alpha);
                drew = true;
            }
        }
        return drew;
    }

    private static void Sample(
        TileWorldRasterSourceFrame frame,
        double u,
        double v,
        TileWorldRasterSampling sampling,
        out byte red,
        out byte green,
        out byte blue,
        out byte alpha)
    {
        ReadOnlySpan<byte> pixels = frame.RgbaPixels.Span;
        if (sampling == TileWorldRasterSampling.PixelArt)
        {
            int x = Math.Min(frame.Width - 1, (int)(u * frame.Width));
            int y = Math.Min(frame.Height - 1, (int)(v * frame.Height));
            int index = (y * frame.Width + x) * 4;
            red = pixels[index];
            green = pixels[index + 1];
            blue = pixels[index + 2];
            alpha = pixels[index + 3];
            return;
        }

        double sourceX = u * frame.Width - 0.5d;
        double sourceY = v * frame.Height - 0.5d;
        int x0Unclamped = (int)Math.Floor(sourceX);
        int y0Unclamped = (int)Math.Floor(sourceY);
        int x0 = Math.Clamp(x0Unclamped, 0, frame.Width - 1);
        int y0 = Math.Clamp(y0Unclamped, 0, frame.Height - 1);
        int x1 = Math.Clamp(x0Unclamped + 1, 0, frame.Width - 1);
        int y1 = Math.Clamp(y0Unclamped + 1, 0, frame.Height - 1);
        double tx = Math.Clamp(sourceX - x0Unclamped, 0d, 1d);
        double ty = Math.Clamp(sourceY - y0Unclamped, 0d, 1d);
        red = Interpolate(pixels, frame.Width, x0, y0, x1, y1, tx, ty, 0);
        green = Interpolate(pixels, frame.Width, x0, y0, x1, y1, tx, ty, 1);
        blue = Interpolate(pixels, frame.Width, x0, y0, x1, y1, tx, ty, 2);
        alpha = Interpolate(pixels, frame.Width, x0, y0, x1, y1, tx, ty, 3);
    }

    private static byte Interpolate(
        ReadOnlySpan<byte> pixels,
        int width,
        int x0,
        int y0,
        int x1,
        int y1,
        double tx,
        double ty,
        int channel)
    {
        int topLeft = pixels[(y0 * width + x0) * 4 + channel];
        int topRight = pixels[(y0 * width + x1) * 4 + channel];
        int bottomLeft = pixels[(y1 * width + x0) * 4 + channel];
        int bottomRight = pixels[(y1 * width + x1) * 4 + channel];
        double top = topLeft + (topRight - topLeft) * tx;
        double bottom = bottomLeft + (bottomRight - bottomLeft) * tx;
        return (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * ty), 0, 255);
    }

    private static void BlendSourceOver(
        byte[] pixels,
        int index,
        byte sourceRed,
        byte sourceGreen,
        byte sourceBlue,
        byte sourceAlpha)
    {
        if (sourceAlpha == 255)
        {
            pixels[index] = sourceRed;
            pixels[index + 1] = sourceGreen;
            pixels[index + 2] = sourceBlue;
            pixels[index + 3] = 255;
            return;
        }

        int destinationAlpha = pixels[index + 3];
        int inverse = 255 - sourceAlpha;
        int outAlpha = sourceAlpha + (destinationAlpha * inverse + 127) / 255;
        if (outAlpha == 0) return;
        pixels[index] = BlendChannel(sourceRed, pixels[index], sourceAlpha, destinationAlpha, inverse, outAlpha);
        pixels[index + 1] = BlendChannel(sourceGreen, pixels[index + 1], sourceAlpha, destinationAlpha, inverse, outAlpha);
        pixels[index + 2] = BlendChannel(sourceBlue, pixels[index + 2], sourceAlpha, destinationAlpha, inverse, outAlpha);
        pixels[index + 3] = (byte)outAlpha;
    }

    private static byte BlendChannel(
        int source,
        int destination,
        int sourceAlpha,
        int destinationAlpha,
        int inverse,
        int outAlpha)
    {
        long premultiplied = (long)source * sourceAlpha * 255L +
                             (long)destination * destinationAlpha * inverse;
        return (byte)Math.Clamp((int)((premultiplied + outAlpha * 127L) / (outAlpha * 255L)), 0, 255);
    }

    private static void ExtrudeGutter(byte[] pixels, int width, int height, int gutter)
    {
        if (gutter == 0) return;
        int encodedWidth = checked(width + gutter * 2);
        int stride = checked(encodedWidth * 4);
        for (int y = 0; y < height; y++)
        {
            int row = (y + gutter) * stride;
            int left = row + gutter * 4;
            int right = row + (gutter + width - 1) * 4;
            for (int x = 0; x < gutter; x++)
            {
                pixels.AsSpan(left, 4).CopyTo(pixels.AsSpan(row + x * 4, 4));
                pixels.AsSpan(right, 4).CopyTo(pixels.AsSpan(row + (gutter + width + x) * 4, 4));
            }
        }
        int firstRow = gutter * stride;
        int lastRow = (gutter + height - 1) * stride;
        for (int y = 0; y < gutter; y++)
        {
            pixels.AsSpan(firstRow, stride).CopyTo(pixels.AsSpan(y * stride, stride));
            pixels.AsSpan(lastRow, stride).CopyTo(pixels.AsSpan((gutter + height + y) * stride, stride));
        }
    }

    private static void ValidateInputs(
        TileMap map,
        TileSetLibrary tileSets,
        TileWorldMetadata metadata)
    {
        if (!StringComparer.Ordinal.Equals(map.Name, metadata.Name) ||
            map.ChunkWidth != metadata.ChunkWidth || map.ChunkHeight != metadata.ChunkHeight ||
            map.Layers.Count != metadata.Layers.Count)
            throw new ArgumentException("TileMap does not match TileWorld metadata.", nameof(map));
        for (int i = 0; i < map.Layers.Count; i++)
        {
            TileLayer layer = map.Layers[i];
            TileWorldLayerMetadata layerMetadata = metadata.Layers[i];
            if (!StringComparer.Ordinal.Equals(layer.Name, layerMetadata.Name) ||
                layer.TileSet != layerMetadata.TileSet || layer.Depth != layerMetadata.Depth ||
                layer.Offset != layerMetadata.Offset || layer.Visible != layerMetadata.Visible)
                throw new ArgumentException("TileMap layers do not match TileWorld metadata.", nameof(map));
            TileSet tileSet = tileSets.Get(layer.TileSet);
            if (tileSet.TileSize != metadata.TileSize)
                throw new ArgumentException("TileSet size does not match TileWorld metadata.", nameof(tileSets));
        }
    }

    private static int FloorToInt(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue + 2d || value > int.MaxValue - 2d)
            throw new InvalidDataException("Rasterized Tile range exceeds Int32 coordinates.");
        return (int)Math.Floor(value);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }
}
