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
        VerifyRasterLodAndArchive();
        VerifyBoundsAndFormatValidation();
        VerifyIntegrityAndOwnership();
        Console.WriteLine(_failures == 0
            ? "=== All TileWorlds smoke tests passed ==="
            : $"=== {_failures} TileWorlds test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyRasterLodAndArchive()
    {
        Console.WriteLine("2. Deterministic per-Layer raster LODs");
        TileSetLibrary tileSets = CreateTileSets();
        var map = new TileMap("world.raster", 2, 2);
        TileLayer ground = map.AddLayer("ground", new TileSetRef("world.tiles"), -4);
        TileLayer overlay = map.AddLayer("overlay", new TileSetRef("world.tiles"), 2);
        _ = map.AddLayer("hidden", new TileSetRef("world.tiles"), 4, visible: false);
        ground.SetCell(-1, 0, new TileCell(new TileId(1)));
        ground.SetCell(0, 0, new TileCell(new TileId(1)));
        ground.SetCell(1, 0, new TileCell(new TileId(1), TileTransform.FlipX));
        ground.SetCell(0, 1, new TileCell(new TileId(1), TileTransform.Rotate90));
        overlay.SetCell(0, 0, new TileCell(new TileId(1)));

        var settings = new TileWorldRasterSettings(8, 8, 1, TileWorldRasterSampling.PixelArt);
        TileWorldArchiveBuild lod0 = TileWorldArchiveBuilder.BuildLod0(
            map,
            tileSets,
            new TileWorldChunkBounds(-1, 0, 1, 1),
            declaredLodCount: 3,
            settings);
        TileWorldRasterChunkImage[] images = TileWorldRasterizer.RasterizeLodLevels(
            map, tileSets, lod0.Metadata, new TestRasterSource()).ToArray();
        Check(images.Select(image => image.Key).SequenceEqual([
                new TileWorldChunkKey(1, -1, 0),
                new TileWorldChunkKey(1, 0, 0),
                new TileWorldChunkKey(2, -1, 0),
                new TileWorldChunkKey(2, 0, 0)
            ]),
            "Power-of-two LOD coverage retains deterministic negative Chunk coordinates");

        TileWorldRasterChunkImage levelOne = images.Single(image =>
            image.Key == new TileWorldChunkKey(1, 0, 0));
        Check(levelOne.Layers.Select(layer => layer.LayerIndex).SequenceEqual([0, 1]),
            "Visible Layers remain separate and hidden Layers are omitted");
        TileWorldRasterLayerImage pixels = levelOne.Layers[0];
        Check(GetPixel(pixels, 0, 0) == new Rgba(255, 0, 0, 255) &&
              GetPixel(pixels, 1, 0) == new Rgba(0, 255, 0, 255) &&
              GetPixel(pixels, 2, 0) == new Rgba(0, 255, 0, 255) &&
              GetPixel(pixels, 3, 0) == new Rgba(255, 0, 0, 255),
            "PixelArt rasterization matches normal and horizontal-flip Tile geometry");
        Check(GetPixel(pixels, 0, 2) == new Rgba(0, 255, 0, 255),
            "Positive quarter-turn rasterization matches the engine's Y-down CCW convention");
        Check(GetEncodedPixel(pixels, 0, 0) == GetPixel(pixels, 0, 0) &&
              GetEncodedPixel(pixels, pixels.EncodedWidth - 1, 0) == GetPixel(pixels, pixels.Width - 1, 0),
            "Gutter pixels deterministically extrude the inner image edges");

        TileWorldRasterChunkData[] rasterChunks = images.Select(image =>
            new TileWorldRasterChunkData(
                image.Key,
                image.Layers.Select(layer => new TileWorldRasterLayerData(
                    layer.LayerIndex,
                    layer.Width,
                    layer.Height,
                    layer.Gutter,
                    TileWorldRasterEncoding.WebpLossless,
                    FakeWebp(layer.RgbaPixels))))).ToArray();
        var fallbackSurface = new TileWorldFallbackSurfaceData(
            0,
            2,
            1,
            TileWorldRasterEncoding.WebpLossless,
            TileWorldRasterSampling.Smooth,
            FakeWebp([1, 2, 3, 4]));
        var metadata = new TileWorldMetadata(
            lod0.Metadata.Name,
            lod0.Metadata.ChunkWidth,
            lod0.Metadata.ChunkHeight,
            lod0.Metadata.TileSize,
            lod0.Metadata.Bounds,
            lod0.Metadata.DeclaredLodCount,
            lod0.Metadata.RasterSettings,
            lod0.Metadata.Layers,
            [fallbackSurface.Metadata]);
        var build = new TileWorldArchiveBuild(
            metadata,
            lod0.Chunks,
            rasterChunks,
            [fallbackSurface]);
        byte[] first = Write(build);
        byte[] second = Write(build);
        using var reader = new TileWorldArchiveReader(new MemoryStream(first, writable: false));
        TileWorldRasterChunkData decoded = reader.ReadRasterChunk(new TileWorldChunkKey(1, 0, 0));
        TileWorldFallbackSurfaceData decodedFallback = reader.ReadFallbackSurface(0);
        Check(first.SequenceEqual(second) && reader.ChunkCount == lod0.Chunks.Count + rasterChunks.Length &&
              reader.FallbackSurfaceCount == 1 &&
              decodedFallback.Metadata == fallbackSurface.Metadata &&
              decodedFallback.EncodedBytes.SequenceEqual(fallbackSurface.EncodedBytes) &&
              reader.Metadata.TileSize == new Vector2(16, 16) &&
              reader.Metadata.RasterSettings == settings &&
              decoded.Layers.Count == 2 && decoded.Layers[0].EncodedBytes.SequenceEqual(
                  rasterChunks.Single(chunk => chunk.Key == decoded.Key).Layers[0].EncodedBytes),
            "Raster metadata, fallback surfaces and per-Layer payloads round-trip deterministically");
        CheckThrows<InvalidOperationException>(
            () => reader.ReadChunk(new TileWorldChunkKey(1, 0, 0)),
            "Raster payloads cannot be decoded through the authoritative Tile API");

        var smoothTileSets = new TileSetLibrary();
        smoothTileSets.Register(new TileSet(
            "smooth.tiles",
            new Vector2(2, 2),
            [new TileDefinition(new TileId(1), new SpriteRef("world.ground"))]));
        var smoothMap = new TileMap("world.smooth", 1, 1);
        smoothMap.AddLayer("ground", new TileSetRef("smooth.tiles"))
            .SetCell(0, 0, new TileCell(new TileId(1)));
        TileWorldArchiveBuild smoothLod0 = TileWorldArchiveBuilder.BuildLod0(
            smoothMap,
            smoothTileSets,
            new TileWorldChunkBounds(0, 0, 0, 0),
            2,
            new TileWorldRasterSettings(2, 2, 0, TileWorldRasterSampling.Smooth));
        TileWorldRasterLayerImage smooth = TileWorldRasterizer.RasterizeLodLevels(
            smoothMap, smoothTileSets, smoothLod0.Metadata, new TestRasterSource())
            .Single().Layers.Single();
        Check(GetPixel(smooth, 0, 0) == new Rgba(128, 128, 128, 255),
            "Smooth sampling deterministically bilinearly filters a downsampled Sprite frame");
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
        Console.WriteLine("3. Bounds and strict archive validation");
        TileSetLibrary tileSets = CreateTileSets();
        var map = new TileMap("world.bounds", 2, 2);
        map.AddLayer("ground", new TileSetRef("world.tiles"))
            .SetCell(4, 0, new TileCell(new TileId(1)));
        CheckThrows<InvalidDataException>(() => TileWorldArchiveBuilder.BuildLod0(
                map, tileSets, new TileWorldChunkBounds(0, 0, 1, 1)),
            "Source Chunks outside declared world bounds are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => new TileWorldMetadata(
                "world.invalid-raster",
                2,
                2,
                new Vector2(16, 16),
                new TileWorldChunkBounds(0, 0, 0, 0),
                1,
                default,
                [new TileWorldLayerMetadata(
                    "ground", new TileSetRef("world.tiles"), 0, Vector2.Zero, true)]),
            "Default Raster settings cannot bypass metadata validation");

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
        Console.WriteLine("4. Payload integrity and stream ownership");
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

    private static Rgba GetPixel(TileWorldRasterLayerImage image, int x, int y) =>
        GetEncodedPixel(image, x + image.Gutter, y + image.Gutter);

    private static Rgba GetEncodedPixel(TileWorldRasterLayerImage image, int x, int y)
    {
        int index = (y * image.EncodedWidth + x) * 4;
        return new Rgba(
            image.RgbaPixels[index],
            image.RgbaPixels[index + 1],
            image.RgbaPixels[index + 2],
            image.RgbaPixels[index + 3]);
    }

    private static byte[] FakeWebp(byte[] payload)
    {
        byte[] result = new byte[checked(12 + payload.Length)];
        "RIFF"u8.CopyTo(result);
        "WEBP"u8.CopyTo(result.AsSpan(8));
        payload.CopyTo(result, 12);
        return result;
    }

    private readonly record struct Rgba(byte Red, byte Green, byte Blue, byte Alpha);

    private sealed class TestRasterSource : ITileWorldRasterSource
    {
        private static readonly byte[] Pixels =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255
        ];

        public bool TryResolve(SpriteRef sprite, int subImage, out TileWorldRasterSourceFrame frame)
        {
            if (sprite.Name is "world.ground" or "world.wall")
            {
                frame = new TileWorldRasterSourceFrame(2, 2, Pixels);
                return true;
            }
            frame = default;
            return false;
        }
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
