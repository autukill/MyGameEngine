namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;

public sealed class TileWorldArchiveReader : IDisposable
{
    private sealed record Entry(
        TileWorldChunkPayloadKind Kind,
        long Offset,
        int Length,
        byte[] Hash);

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<TileWorldChunkKey, Entry> _entries = [];
    private bool _disposed;

    public TileWorldMetadata Metadata { get; }
    public int ChunkCount => _entries.Count;

    public TileWorldArchiveReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("TileWorld source must be readable and seekable.", nameof(stream));
        _stream = stream;
        _leaveOpen = leaveOpen;
        try
        {
            Span<byte> magic = stackalloc byte[8];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual(TileWorldArchiveFormat.Magic))
                throw new InvalidDataException("TileWorld archive magic is invalid.");
            int version = TileWorldArchiveFormat.ReadInt32(stream);
            if (version != TileWorldArchiveFormat.Version)
                throw new InvalidDataException($"Unsupported TileWorld archive version '{version}'.");
            string name = ReadString(stream);
            int chunkWidth = TileWorldArchiveFormat.ReadInt32(stream);
            int chunkHeight = TileWorldArchiveFormat.ReadInt32(stream);
            var bounds = new TileWorldChunkBounds(
                TileWorldArchiveFormat.ReadInt32(stream),
                TileWorldArchiveFormat.ReadInt32(stream),
                TileWorldArchiveFormat.ReadInt32(stream),
                TileWorldArchiveFormat.ReadInt32(stream));
            int lodCount = TileWorldArchiveFormat.ReadInt32(stream);
            int layerCount = TileWorldArchiveFormat.ReadInt32(stream);
            if (layerCount is <= 0 or > TileWorldArchiveFormat.MaximumLayers)
                throw new InvalidDataException("TileWorld layer count exceeds the format limit.");
            var layers = new TileWorldLayerMetadata[layerCount];
            for (int i = 0; i < layers.Length; i++)
            {
                string layerName = ReadString(stream);
                string tileSetName = ReadString(stream);
                int depth = TileWorldArchiveFormat.ReadInt32(stream);
                float offsetX = BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream));
                float offsetY = BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream));
                int visible = stream.ReadByte();
                if (visible is not 0 and not 1)
                    throw new InvalidDataException("TileWorld layer visibility is invalid.");
                layers[i] = new TileWorldLayerMetadata(
                    layerName, new TileSetRef(tileSetName), depth, new Vector2(offsetX, offsetY), visible == 1);
            }
            Metadata = new TileWorldMetadata(name, chunkWidth, chunkHeight, bounds, lodCount, layers);
            int chunkCount = TileWorldArchiveFormat.ReadInt32(stream);
            if (chunkCount is < 0 or > TileWorldArchiveFormat.MaximumChunks)
                throw new InvalidDataException("TileWorld Chunk count exceeds the format limit.");
            long indexEnd = checked(stream.Position + (long)chunkCount * TileWorldArchiveFormat.EntryLength);
            if (indexEnd > stream.Length)
                throw new InvalidDataException("TileWorld Chunk index is truncated.");
            long previousEnd = indexEnd;
            TileWorldChunkKey? previousKey = null;
            for (int i = 0; i < chunkCount; i++)
            {
                var key = new TileWorldChunkKey(
                    TileWorldArchiveFormat.ReadInt32(stream),
                    TileWorldArchiveFormat.ReadInt32(stream),
                    TileWorldArchiveFormat.ReadInt32(stream));
                int kindValue = TileWorldArchiveFormat.ReadInt32(stream);
                if (!Enum.IsDefined((TileWorldChunkPayloadKind)kindValue))
                    throw new InvalidDataException($"TileWorld Chunk '{key}' has an unknown payload kind.");
                var kind = (TileWorldChunkPayloadKind)kindValue;
                long offset = TileWorldArchiveFormat.ReadInt64(stream);
                int length = TileWorldArchiveFormat.ReadInt32(stream);
                byte[] hash = new byte[TileWorldArchiveFormat.HashLength];
                stream.ReadExactly(hash);
                if (key.Level < 0 || key.Level >= lodCount)
                    throw new InvalidDataException($"TileWorld Chunk key '{key}' has an invalid level.");
                if (previousKey is { } ordered && ordered.CompareTo(key) >= 0)
                    throw new InvalidDataException("TileWorld Chunk index is not in deterministic key order.");
                if (key.Level == 0 && !bounds.Contains(key.X, key.Y))
                    throw new InvalidDataException($"TileWorld Chunk key '{key}' is outside world bounds.");
                if (length < 0 || length > TileWorldArchiveFormat.MaximumPayloadBytes ||
                    offset < indexEnd || offset < previousEnd || checked(offset + length) > stream.Length)
                    throw new InvalidDataException($"TileWorld Chunk '{key}' has invalid payload bounds.");
                if (!_entries.TryAdd(key, new Entry(kind, offset, length, hash)))
                    throw new InvalidDataException($"TileWorld Chunk key '{key}' appears more than once.");
                previousEnd = checked(offset + length);
                previousKey = key;
            }
            if (previousEnd != stream.Length)
                throw new InvalidDataException("TileWorld archive contains unindexed trailing data.");
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or OverflowException or ArgumentException)
        {
            if (!leaveOpen) stream.Dispose();
            throw new InvalidDataException("TileWorld archive index is malformed or truncated.", exception);
        }
        catch
        {
            if (!leaveOpen) stream.Dispose();
            throw;
        }
    }

    public bool Contains(TileWorldChunkKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _entries.ContainsKey(key);
    }

    public TileWorldChunkPayloadKind GetPayloadKind(TileWorldChunkKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _entries.TryGetValue(key, out Entry? entry)
            ? entry.Kind
            : throw new KeyNotFoundException($"TileWorld Chunk '{key}' does not exist.");
    }

    public TileWorldChunkData ReadChunk(TileWorldChunkKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(key, out Entry? entry))
            throw new KeyNotFoundException($"TileWorld Chunk '{key}' does not exist.");
        if (entry.Kind != TileWorldChunkPayloadKind.AuthoritativeTiles)
            throw new InvalidOperationException(
                $"TileWorld Chunk '{key}' is '{entry.Kind}' and cannot be read as authoritative Tile data.");
        byte[] payload = new byte[entry.Length];
        try
        {
            lock (_stream)
            {
                _stream.Position = entry.Offset;
                _stream.ReadExactly(payload);
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"TileWorld Chunk '{key}' was truncated after opening.", exception);
        }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), entry.Hash))
            throw new InvalidDataException($"TileWorld Chunk '{key}' failed its integrity check.");
        try
        {
            return DecodeChunk(key, payload);
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"TileWorld Chunk '{key}' is malformed or truncated.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_leaveOpen) _stream.Dispose();
    }

    private TileWorldChunkData DecodeChunk(TileWorldChunkKey key, byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        int layerCount = TileWorldArchiveFormat.ReadInt32(stream);
        if (layerCount is < 0 || layerCount > Metadata.Layers.Count)
            throw new InvalidDataException("TileWorld Chunk layer count is invalid.");
        var layers = new TileWorldChunkLayerData[layerCount];
        var seen = new HashSet<int>();
        int expectedCells = checked(Metadata.ChunkWidth * Metadata.ChunkHeight);
        for (int i = 0; i < layers.Length; i++)
        {
            int layerIndex = TileWorldArchiveFormat.ReadInt32(stream);
            if ((uint)layerIndex >= (uint)Metadata.Layers.Count || !seen.Add(layerIndex))
                throw new InvalidDataException("TileWorld Chunk references an invalid layer.");
            TileCell[] cells = ReadRuns(stream, expectedCells);
            int collisionCount = TileWorldArchiveFormat.ReadInt32(stream);
            if (collisionCount < 0 || collisionCount > expectedCells)
                throw new InvalidDataException("TileWorld Chunk collision count is invalid.");
            var collisions = new TileWorldCollisionRect[collisionCount];
            for (int collision = 0; collision < collisions.Length; collision++)
            {
                collisions[collision] = new TileWorldCollisionRect(
                    BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream)),
                    BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream)),
                    BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream)),
                    BitConverter.UInt32BitsToSingle(TileWorldArchiveFormat.ReadUInt32(stream)));
                TileWorldCollisionRect rect = collisions[collision];
                if (!float.IsFinite(rect.Left) || !float.IsFinite(rect.Top) ||
                    !float.IsFinite(rect.Right) || !float.IsFinite(rect.Bottom) ||
                    rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    throw new InvalidDataException("TileWorld Chunk contains an invalid collision rectangle.");
            }
            layers[i] = new TileWorldChunkLayerData(layerIndex, cells, collisions);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("TileWorld Chunk payload contains trailing data.");
        return new TileWorldChunkData(key, layers);
    }

    private static TileCell[] ReadRuns(Stream stream, int expectedCells)
    {
        int runCount = TileWorldArchiveFormat.ReadInt32(stream);
        if (runCount is <= 0 || runCount > expectedCells)
            throw new InvalidDataException("TileWorld Chunk RLE run count is invalid.");
        var cells = new TileCell[expectedCells];
        int written = 0;
        for (int run = 0; run < runCount; run++)
        {
            int length = TileWorldArchiveFormat.ReadInt32(stream);
            uint packed = TileWorldArchiveFormat.ReadUInt32(stream);
            if (length <= 0 || length > expectedCells - written)
                throw new InvalidDataException("TileWorld Chunk RLE length is invalid.");
            cells.AsSpan(written, length).Fill(TileWorldArchiveFormat.Unpack(packed));
            written += length;
        }
        if (written != expectedCells)
            throw new InvalidDataException("TileWorld Chunk RLE data does not fill the Chunk.");
        return cells;
    }

    private static string ReadString(Stream stream)
    {
        int length = TileWorldArchiveFormat.ReadInt32(stream);
        if (length is < 0 or > TileWorldArchiveFormat.MaximumStringBytes)
            throw new InvalidDataException("TileWorld string length exceeds the format limit.");
        byte[] bytes = new byte[length];
        stream.ReadExactly(bytes);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("TileWorld contains invalid UTF-8.", exception);
        }
    }
}
