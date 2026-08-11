namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;

public sealed class TileWorldArchiveBuild
{
    public TileWorldArchiveBuild(
        TileWorldMetadata metadata,
        IReadOnlyList<TileWorldChunkData> chunks,
        IReadOnlyList<TileWorldRasterChunkData>? rasterChunks = null,
        IReadOnlyList<TileWorldFallbackSurfaceData>? fallbackSurfaces = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        RasterChunks = rasterChunks ?? [];
        FallbackSurfaces = fallbackSurfaces ?? [];
    }

    public TileWorldMetadata Metadata { get; }
    public IReadOnlyList<TileWorldChunkData> Chunks { get; }
    public IReadOnlyList<TileWorldRasterChunkData> RasterChunks { get; }
    public IReadOnlyList<TileWorldFallbackSurfaceData> FallbackSurfaces { get; }
    public int TotalChunkCount => checked(Chunks.Count + RasterChunks.Count);
}

public static class TileWorldArchiveWriter
{
    private sealed record EncodedChunk(
        TileWorldChunkKey Key,
        TileWorldChunkPayloadKind Kind,
        byte[] Payload,
        byte[] Hash);

    public static void Write(Stream destination, TileWorldArchiveBuild build)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(build);
        if (!destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("TileWorld destination must be writable and seekable.", nameof(destination));

        EncodedChunk[] chunks = build.Chunks
            .Select(chunk => EncodeChunk(build.Metadata, chunk))
            .Concat(build.RasterChunks.Select(chunk => EncodeRasterChunk(build.Metadata, chunk)))
            .OrderBy(chunk => chunk.Key)
            .ToArray();
        TileWorldFallbackSurfaceData[] fallbackSurfaces = build.FallbackSurfaces
            .OrderBy(surface => surface.LayerIndex)
            .ToArray();
        if (chunks.Length > TileWorldArchiveFormat.MaximumChunks)
            throw new InvalidDataException("TileWorld contains too many Chunk payloads.");
        if (chunks.Select(chunk => chunk.Key).Distinct().Count() != chunks.Length)
            throw new InvalidDataException("TileWorld contains duplicate Chunk keys.");
        if (fallbackSurfaces.Select(surface => surface.LayerIndex).Distinct().Count() !=
            fallbackSurfaces.Length)
            throw new InvalidDataException("TileWorld contains duplicate fallback surface layers.");
        if (!fallbackSurfaces.Select(surface => surface.Metadata).SequenceEqual(
                build.Metadata.FallbackSurfaces))
            throw new InvalidDataException(
                "TileWorld fallback surface payloads do not match metadata.");
        foreach (EncodedChunk chunk in chunks)
        {
            if (chunk.Key.Level >= build.Metadata.DeclaredLodCount)
                throw new InvalidDataException($"Chunk '{chunk.Key}' exceeds declared LOD count.");
            if (!build.Metadata.GetChunkBounds(chunk.Key.Level).Contains(chunk.Key.X, chunk.Key.Y))
                throw new InvalidDataException($"Chunk '{chunk.Key}' is outside TileWorld bounds.");
        }

        using var index = new MemoryStream();
        index.Write(TileWorldArchiveFormat.Magic);
        TileWorldArchiveFormat.WriteInt32(index, TileWorldArchiveFormat.Version);
        WriteString(index, build.Metadata.Name);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.ChunkWidth);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.ChunkHeight);
        TileWorldArchiveFormat.WriteUInt32(index, BitConverter.SingleToUInt32Bits(build.Metadata.TileSize.X));
        TileWorldArchiveFormat.WriteUInt32(index, BitConverter.SingleToUInt32Bits(build.Metadata.TileSize.Y));
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MinX);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MinY);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MaxX);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MaxY);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.DeclaredLodCount);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.RasterSettings.Width);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.RasterSettings.Height);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.RasterSettings.Gutter);
        TileWorldArchiveFormat.WriteInt32(index, (int)build.Metadata.RasterSettings.Sampling);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Layers.Count);
        foreach (TileWorldLayerMetadata layer in build.Metadata.Layers)
        {
            WriteString(index, layer.Name);
            WriteString(index, layer.TileSet.Name);
            TileWorldArchiveFormat.WriteInt32(index, layer.Depth);
            TileWorldArchiveFormat.WriteUInt32(index, BitConverter.SingleToUInt32Bits(layer.Offset.X));
            TileWorldArchiveFormat.WriteUInt32(index, BitConverter.SingleToUInt32Bits(layer.Offset.Y));
            index.WriteByte(layer.Visible ? (byte)1 : (byte)0);
        }
        TileWorldArchiveFormat.WriteInt32(index, fallbackSurfaces.Length);
        foreach (TileWorldFallbackSurfaceData surface in fallbackSurfaces)
        {
            TileWorldArchiveFormat.WriteInt32(index, surface.LayerIndex);
            TileWorldArchiveFormat.WriteInt32(index, surface.Width);
            TileWorldArchiveFormat.WriteInt32(index, surface.Height);
            TileWorldArchiveFormat.WriteInt32(index, (int)surface.Encoding);
            TileWorldArchiveFormat.WriteInt32(index, (int)surface.Sampling);
            TileWorldArchiveFormat.WriteInt32(index, surface.EncodedBytes.Length);
            index.Write(SHA256.HashData(surface.EncodedBytes));
        }
        TileWorldArchiveFormat.WriteInt32(index, chunks.Length);
        long payloadOffset = checked(
            index.Length +
            (long)chunks.Length * TileWorldArchiveFormat.EntryLength +
            fallbackSurfaces.Sum(surface => (long)surface.EncodedBytes.Length));
        foreach (EncodedChunk chunk in chunks)
        {
            TileWorldArchiveFormat.WriteInt32(index, chunk.Key.Level);
            TileWorldArchiveFormat.WriteInt32(index, chunk.Key.X);
            TileWorldArchiveFormat.WriteInt32(index, chunk.Key.Y);
            TileWorldArchiveFormat.WriteInt32(index, (int)chunk.Kind);
            TileWorldArchiveFormat.WriteInt64(index, payloadOffset);
            TileWorldArchiveFormat.WriteInt32(index, chunk.Payload.Length);
            index.Write(chunk.Hash);
            payloadOffset = checked(payloadOffset + chunk.Payload.Length);
        }

        destination.SetLength(0);
        destination.Position = 0;
        index.Position = 0;
        index.CopyTo(destination);
        foreach (TileWorldFallbackSurfaceData surface in fallbackSurfaces)
            destination.Write(surface.EncodedBytes);
        foreach (EncodedChunk chunk in chunks) destination.Write(chunk.Payload);
        destination.Flush();
    }

    private static EncodedChunk EncodeChunk(TileWorldMetadata metadata, TileWorldChunkData chunk)
    {
        if (chunk.Key.Level != 0)
            throw new NotSupportedException("Authoritative Tile payloads are only valid at LOD0.");
        using var payload = new MemoryStream();
        TileWorldArchiveFormat.WriteInt32(payload, chunk.Layers.Count);
        int expectedCells = checked(metadata.ChunkWidth * metadata.ChunkHeight);
        foreach (TileWorldChunkLayerData layer in chunk.Layers.OrderBy(layer => layer.LayerIndex))
        {
            if ((uint)layer.LayerIndex >= (uint)metadata.Layers.Count)
                throw new InvalidDataException("Chunk references an unknown layer index.");
            if (layer.Cells.Length != expectedCells)
                throw new InvalidDataException("LOD0 Chunk layer has an invalid cell count.");
            TileWorldArchiveFormat.WriteInt32(payload, layer.LayerIndex);
            WriteRuns(payload, layer.Cells);
            TileWorldArchiveFormat.WriteInt32(payload, layer.CollisionRects.Length);
            foreach (TileWorldCollisionRect rect in layer.CollisionRects)
            {
                if (!float.IsFinite(rect.Left) || !float.IsFinite(rect.Top) ||
                    !float.IsFinite(rect.Right) || !float.IsFinite(rect.Bottom) ||
                    rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    throw new InvalidDataException("Chunk contains an invalid collision rectangle.");
                TileWorldArchiveFormat.WriteUInt32(payload, BitConverter.SingleToUInt32Bits(rect.Left));
                TileWorldArchiveFormat.WriteUInt32(payload, BitConverter.SingleToUInt32Bits(rect.Top));
                TileWorldArchiveFormat.WriteUInt32(payload, BitConverter.SingleToUInt32Bits(rect.Right));
                TileWorldArchiveFormat.WriteUInt32(payload, BitConverter.SingleToUInt32Bits(rect.Bottom));
            }
        }
        if (payload.Length > TileWorldArchiveFormat.MaximumPayloadBytes)
            throw new InvalidDataException("TileWorld Chunk payload exceeds the format limit.");
        byte[] bytes = payload.ToArray();
        return new EncodedChunk(
            chunk.Key,
            TileWorldChunkPayloadKind.AuthoritativeTiles,
            bytes,
            SHA256.HashData(bytes));
    }

    private static EncodedChunk EncodeRasterChunk(
        TileWorldMetadata metadata,
        TileWorldRasterChunkData chunk)
    {
        if (chunk.Key.Level <= 0)
            throw new InvalidDataException("Raster Chunk levels must be greater than zero.");
        long payloadLength = 4;
        foreach (TileWorldRasterLayerData layer in chunk.Layers)
        {
            payloadLength = checked(payloadLength + 24L + layer.EncodedBytes.Length);
            if (payloadLength > TileWorldArchiveFormat.MaximumPayloadBytes)
                throw new InvalidDataException("TileWorld Raster Chunk payload exceeds the format limit.");
        }
        using var payload = new MemoryStream((int)payloadLength);
        TileWorldArchiveFormat.WriteInt32(payload, chunk.Layers.Count);
        foreach (TileWorldRasterLayerData layer in chunk.Layers.OrderBy(layer => layer.LayerIndex))
        {
            if ((uint)layer.LayerIndex >= (uint)metadata.Layers.Count ||
                !metadata.Layers[layer.LayerIndex].Visible)
                throw new InvalidDataException("Raster Chunk references an unknown or invisible layer.");
            if (layer.Width != metadata.RasterSettings.Width ||
                layer.Height != metadata.RasterSettings.Height ||
                layer.Gutter != metadata.RasterSettings.Gutter)
                throw new InvalidDataException("Raster Chunk dimensions do not match TileWorld metadata.");
            ValidateEncodedRaster(layer);
            TileWorldArchiveFormat.WriteInt32(payload, layer.LayerIndex);
            TileWorldArchiveFormat.WriteInt32(payload, (int)layer.Encoding);
            TileWorldArchiveFormat.WriteInt32(payload, layer.Width);
            TileWorldArchiveFormat.WriteInt32(payload, layer.Height);
            TileWorldArchiveFormat.WriteInt32(payload, layer.Gutter);
            TileWorldArchiveFormat.WriteInt32(payload, layer.EncodedBytes.Length);
            payload.Write(layer.EncodedBytes);
        }
        if (payload.Length > TileWorldArchiveFormat.MaximumPayloadBytes)
            throw new InvalidDataException("TileWorld Raster Chunk payload exceeds the format limit.");
        byte[] bytes = payload.ToArray();
        return new EncodedChunk(
            chunk.Key,
            TileWorldChunkPayloadKind.RasterLayers,
            bytes,
            SHA256.HashData(bytes));
    }

    private static void ValidateEncodedRaster(TileWorldRasterLayerData layer)
    {
        if (layer.Encoding != TileWorldRasterEncoding.WebpLossless ||
            layer.EncodedBytes.Length < 12 ||
            !layer.EncodedBytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !layer.EncodedBytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Raster layer is not a WebP payload.");
    }

    private static void WriteRuns(Stream stream, ReadOnlySpan<TileCell> cells)
    {
        int runCount = 0;
        for (int index = 0; index < cells.Length;)
        {
            uint value = TileWorldArchiveFormat.Pack(cells[index]);
            int length = 1;
            while (index + length < cells.Length &&
                   TileWorldArchiveFormat.Pack(cells[index + length]) == value)
                length++;
            runCount++;
            index += length;
        }
        TileWorldArchiveFormat.WriteInt32(stream, runCount);
        for (int index = 0; index < cells.Length;)
        {
            uint value = TileWorldArchiveFormat.Pack(cells[index]);
            int length = 1;
            while (index + length < cells.Length &&
                   TileWorldArchiveFormat.Pack(cells[index + length]) == value)
                length++;
            TileWorldArchiveFormat.WriteInt32(stream, length);
            TileWorldArchiveFormat.WriteUInt32(stream, value);
            index += length;
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > TileWorldArchiveFormat.MaximumStringBytes)
            throw new InvalidDataException("TileWorld string exceeds the format limit.");
        TileWorldArchiveFormat.WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }
}
