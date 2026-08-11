namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;

public sealed record TileWorldArchiveBuild(
    TileWorldMetadata Metadata,
    IReadOnlyList<TileWorldChunkData> Chunks);

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
            .OrderBy(chunk => chunk.Key)
            .Select(chunk => EncodeChunk(build.Metadata, chunk))
            .ToArray();
        if (chunks.Length > TileWorldArchiveFormat.MaximumChunks)
            throw new InvalidDataException("TileWorld contains too many Chunk payloads.");
        if (chunks.Select(chunk => chunk.Key).Distinct().Count() != chunks.Length)
            throw new InvalidDataException("TileWorld contains duplicate Chunk keys.");
        foreach (EncodedChunk chunk in chunks)
        {
            if (chunk.Key.Level >= build.Metadata.DeclaredLodCount)
                throw new InvalidDataException($"Chunk '{chunk.Key}' exceeds declared LOD count.");
            if (!build.Metadata.Bounds.Contains(chunk.Key.X, chunk.Key.Y) && chunk.Key.Level == 0)
                throw new InvalidDataException($"Chunk '{chunk.Key}' is outside TileWorld bounds.");
        }

        using var index = new MemoryStream();
        index.Write(TileWorldArchiveFormat.Magic);
        TileWorldArchiveFormat.WriteInt32(index, TileWorldArchiveFormat.Version);
        WriteString(index, build.Metadata.Name);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.ChunkWidth);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.ChunkHeight);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MinX);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MinY);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MaxX);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.Bounds.MaxY);
        TileWorldArchiveFormat.WriteInt32(index, build.Metadata.DeclaredLodCount);
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
        TileWorldArchiveFormat.WriteInt32(index, chunks.Length);
        long payloadOffset = checked(index.Length + (long)chunks.Length * TileWorldArchiveFormat.EntryLength);
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
        foreach (EncodedChunk chunk in chunks) destination.Write(chunk.Payload);
        destination.Flush();
    }

    private static EncodedChunk EncodeChunk(TileWorldMetadata metadata, TileWorldChunkData chunk)
    {
        if (chunk.Key.Level != 0)
            throw new NotSupportedException("TileWorld archive v1 currently writes authoritative LOD0 chunks only.");
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
