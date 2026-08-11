namespace TileWorlds.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== TileWorlds Feature Smoke Tests ===");
        VerifyDeterministicRoundTrip();
        VerifyBoundsAndFormatValidation();
        VerifyIntegrityAndOwnership();
        Console.WriteLine(_failures == 0
            ? "=== All TileWorlds smoke tests passed ==="
            : $"=== {_failures} TileWorlds test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyDeterministicRoundTrip()
    {
        Console.WriteLine("1. Deterministic LOD0 archive round-trip");
        TileSetLibrary tileSets = CreateTileSets();
        var map = new TileMap("world.demo", 4, 2);
        TileLayer ground = map.AddLayer("ground", new TileSetRef("world.tiles"), -4, new Vector2(8, 16));
        TileLayer walls = map.AddLayer("walls", new TileSetRef("world.tiles"), 2);
        ground.SetCell(-1, 0, new TileCell(new TileId(1), TileTransform.FlipX));
        ground.SetCell(0, 0, new TileCell(new TileId(1)));
        walls.SetCell(0, 0, new TileCell(new TileId(2)));
        walls.SetCell(1, 0, new TileCell(new TileId(2)));
        walls.SetCell(0, 1, new TileCell(new TileId(2)));
        walls.SetCell(1, 1, new TileCell(new TileId(2)));

        TileWorldArchiveBuild build = TileWorldArchiveBuilder.BuildLod0(
            map, tileSets, new TileWorldChunkBounds(-1, 0, 2, 1), declaredLodCount: 4);
        byte[] first = Write(build);
        byte[] second = Write(build);
        Check(first.SequenceEqual(second), "Identical input produces byte-identical archives");

        using var reader = new TileWorldArchiveReader(new MemoryStream(first, writable: false));
        Check(reader.Metadata.Name == "world.demo" && reader.Metadata.DeclaredLodCount == 4 &&
              reader.Metadata.Bounds == new TileWorldChunkBounds(-1, 0, 2, 1) &&
              reader.Metadata.Layers[0].Offset == new Vector2(8, 16) && reader.ChunkCount == 2,
            "Archive index retains bounds, layers, ordering, and future LOD declaration");
        TileWorldChunkData negative = reader.ReadChunk(new TileWorldChunkKey(0, -1, 0));
        Check(negative.Layers.Count == 1 &&
              negative.Layers[0].Cells[3] == new TileCell(new TileId(1), TileTransform.FlipX),
            "Negative Chunk coordinates and transformed Tile cells round-trip");
        TileWorldChunkData origin = reader.ReadChunk(new TileWorldChunkKey(0, 0, 0));
        TileWorldChunkLayerData wallData = origin.Layers.Single(layer => layer.LayerIndex == 1);
        Check(wallData.Cells[0].Tile == new TileId(2) && wallData.CollisionRects.Length == 1 &&
              wallData.CollisionRects[0] == new TileWorldCollisionRect(0, 0, 32, 32),
            "RLE cells and greedily merged authoritative collision data round-trip");
        Check(reader.GetPayloadKind(new TileWorldChunkKey(0, 0, 0)) ==
              TileWorldChunkPayloadKind.AuthoritativeTiles,
            "Chunk index declares its payload kind before decoding");
        Check(!reader.Contains(new TileWorldChunkKey(1, 0, 0)),
            "Declared future LOD levels do not pretend to contain visual payloads");
    }

    private static void VerifyBoundsAndFormatValidation()
    {
        Console.WriteLine("2. Bounds and strict archive validation");
        TileSetLibrary tileSets = CreateTileSets();
        var map = new TileMap("world.bounds", 2, 2);
        map.AddLayer("ground", new TileSetRef("world.tiles"))
            .SetCell(4, 0, new TileCell(new TileId(1)));
        CheckThrows<InvalidDataException>(() => TileWorldArchiveBuilder.BuildLod0(
                map, tileSets, new TileWorldChunkBounds(0, 0, 1, 1)),
            "Source Chunks outside declared world bounds are rejected");

        TileWorldArchiveBuild valid = TileWorldArchiveBuilder.BuildLod0(
            map, tileSets, new TileWorldChunkBounds(0, 0, 2, 1));
        byte[] bytes = Write(valid);
        byte[] badMagic = (byte[])bytes.Clone();
        badMagic[0] ^= 0xff;
        CheckThrows<InvalidDataException>(() => new TileWorldArchiveReader(
                new MemoryStream(badMagic, writable: false)),
            "Unknown archive magic is rejected");
        byte[] truncated = bytes[..^1];
        CheckThrows<InvalidDataException>(() => new TileWorldArchiveReader(
                new MemoryStream(truncated, writable: false)),
            "Truncated payload bounds are rejected before a Chunk is read");
        byte[] trailing = [.. bytes, 0];
        CheckThrows<InvalidDataException>(() => new TileWorldArchiveReader(
                new MemoryStream(trailing, writable: false)),
            "Unindexed trailing archive bytes are rejected");
    }

    private static void VerifyIntegrityAndOwnership()
    {
        Console.WriteLine("3. Payload integrity and stream ownership");
        TileSetLibrary tileSets = CreateTileSets();
        var map = new TileMap("world.integrity", 2, 2);
        map.AddLayer("ground", new TileSetRef("world.tiles"))
            .SetCell(0, 0, new TileCell(new TileId(1)));
        byte[] bytes = Write(TileWorldArchiveBuilder.BuildLod0(
            map, tileSets, new TileWorldChunkBounds(0, 0, 0, 0)));
        bytes[^1] ^= 0x01;
        using (var reader = new TileWorldArchiveReader(new MemoryStream(bytes, writable: false)))
        {
            CheckThrows<InvalidDataException>(() => reader.ReadChunk(new TileWorldChunkKey(0, 0, 0)),
                "Modified Chunk payload fails SHA-256 integrity validation");
        }

        byte[] valid = Write(TileWorldArchiveBuilder.BuildLod0(
            map, tileSets, new TileWorldChunkBounds(0, 0, 0, 0)));
        var borrowed = new MemoryStream(valid, writable: false);
        using (var reader = new TileWorldArchiveReader(borrowed, leaveOpen: true)) { }
        Check(borrowed.CanRead, "leaveOpen preserves borrowed archive streams");
        borrowed.Dispose();
    }

    private static TileSetLibrary CreateTileSets()
    {
        var library = new TileSetLibrary();
        library.Register(new TileSet(
            "world.tiles",
            new Vector2(16, 16),
            [
                new TileDefinition(new TileId(1), new SpriteRef("world.ground")),
                new TileDefinition(
                    new TileId(2), new SpriteRef("world.wall"), Collision: TileCollisionKind.Solid)
            ]));
        return library;
    }

    private static byte[] Write(TileWorldArchiveBuild build)
    {
        using var stream = new MemoryStream();
        TileWorldArchiveWriter.Write(stream, build);
        return stream.ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (condition) Console.WriteLine($"  [PASS] {message}");
        else
        {
            Console.WriteLine($"  [FAIL] {message}");
            _failures++;
        }
    }

    private static void CheckThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
            Check(false, message);
        }
        catch (TException)
        {
            Check(true, message);
        }
    }
}
