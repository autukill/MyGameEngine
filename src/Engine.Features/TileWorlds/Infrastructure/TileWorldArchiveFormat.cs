namespace GameEngine.Features.TileWorlds.Infrastructure;

using System.Buffers.Binary;
using GameEngine.Features.Tilemaps.Domain;

internal static class TileWorldArchiveFormat
{
    public static ReadOnlySpan<byte> Magic => "MGWORLD\0"u8;
    public const int Version = 3;
    public const int HashLength = 32;
    public const int EntryLength = 4 + 4 + 4 + 4 + 8 + 4 + HashLength;
    public const int MaximumStringBytes = 16 * 1024;
    public const int MaximumLayers = 1_024;
    public const int MaximumChunks = 4_000_000;
    public const int MaximumPayloadBytes = 256 * 1024 * 1024;

    public static uint Pack(TileCell cell)
    {
        if (((byte)cell.Transform & 0xf0) != 0 ||
            (cell.Tile.IsEmpty && cell.Transform != TileTransform.None))
            throw new InvalidDataException("Tile cell contains unsupported transform data.");
        return cell.Tile.Value | ((uint)(byte)cell.Transform << 16);
    }

    public static TileCell Unpack(uint value)
    {
        if ((value & 0xfff0_0000u) != 0)
            throw new InvalidDataException("Tile cell contains unsupported transform bits.");
        var transform = (TileTransform)((value >> 16) & 0x0f);
        if ((value & 0xffffu) == 0 && transform != TileTransform.None)
            throw new InvalidDataException("Empty Tile cells cannot carry transforms.");
        return new TileCell(new TileId((ushort)value), transform);
    }

    public static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    public static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    public static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    public static int ReadInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public static long ReadInt64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }
}
