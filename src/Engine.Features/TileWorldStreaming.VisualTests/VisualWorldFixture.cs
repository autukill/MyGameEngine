namespace TileWorldStreaming.VisualTests;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using Imazen.WebP;
using Imazen.WebP.Extern;

internal sealed class VisualWorldFixture : IDisposable
{
    public const int WorldChunkCount = 4;
    public const int TileWorldSize = 1_024;
    public const int TilePixelSize = 16;
    private const int RasterSize = 256;
    private const int RasterGutter = 2;
    private readonly string _directory;

    public VisualWorldFixture()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "mygame-tileworld-visual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        try
        {
            TileSet = CreateTileSet();
            var tileSets = new TileSetLibrary();
            tileSets.Register(TileSet);
            TileMap map = CreateMap(TileSet.Ref);
            TileWorldArchiveBuild lod0 = TileWorldArchiveBuilder.BuildLod0(
                map,
                tileSets,
                new TileWorldChunkBounds(0, 0, WorldChunkCount - 1, WorldChunkCount - 1),
                declaredLodCount: 3,
                new TileWorldRasterSettings(
                    RasterSize,
                    RasterSize,
                    RasterGutter,
                    TileWorldRasterSampling.Smooth));

            var fallback = new TileWorldFallbackSurfaceData(
                layerIndex: 0,
                width: 128,
                height: 128,
                TileWorldRasterEncoding.WebpLossless,
                TileWorldRasterSampling.Smooth,
                EncodeRgba(CreateSurfacePixels(
                    width: 128,
                    height: 128,
                    gutter: 0,
                    level: 2,
                    chunkX: 0,
                    chunkY: 0,
                    layerIndex: 0,
                    preview: true), 128, 128));

            var rasterChunks = new List<TileWorldRasterChunkData>();
            AddRasterLevel(rasterChunks, level: 1, maximumCoordinate: 1);
            AddRasterLevel(rasterChunks, level: 2, maximumCoordinate: 0);
            var metadata = new TileWorldMetadata(
                lod0.Metadata.Name,
                lod0.Metadata.ChunkWidth,
                lod0.Metadata.ChunkHeight,
                lod0.Metadata.TileSize,
                lod0.Metadata.Bounds,
                lod0.Metadata.DeclaredLodCount,
                lod0.Metadata.RasterSettings,
                lod0.Metadata.Layers,
                [fallback.Metadata]);

            string archivePath = Path.Combine(_directory, "visual-world.mgworld");
            using (FileStream stream = File.Create(archivePath))
            {
                TileWorldArchiveWriter.Write(
                    stream,
                    new TileWorldArchiveBuild(metadata, lod0.Chunks, rasterChunks, [fallback]));
            }
            Descriptor = new TileWorldDescriptor(metadata.Ref, archivePath, metadata);
        }
        catch
        {
            Directory.Delete(_directory, recursive: true);
            throw;
        }
    }

    public TileWorldDescriptor Descriptor { get; }
    public TileSet TileSet { get; }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    public static byte[] CreateTilePixels(int variant)
    {
        if (variant is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(variant));
        (byte R, byte G, byte B)[] palette =
        [
            (52, 145, 92),
            (195, 155, 70),
            (90, 112, 132),
            (38, 105, 160)
        ];
        (byte r, byte g, byte b) = palette[variant];
        var pixels = new byte[TilePixelSize * TilePixelSize * 4];
        for (int y = 0; y < TilePixelSize; y++)
        for (int x = 0; x < TilePixelSize; x++)
        {
            float light = ((x + y + variant) % 5 == 0 ? 1.18f : 1f) *
                          (x is 0 or TilePixelSize - 1 || y is 0 or TilePixelSize - 1 ? .72f : 1f);
            WritePixel(pixels, TilePixelSize, x, y,
                Scale(r, light), Scale(g, light), Scale(b, light), 255);
        }
        return pixels;
    }

    public static byte[] CreateRoadPixels()
    {
        var pixels = new byte[TilePixelSize * TilePixelSize * 4];
        for (int y = 0; y < TilePixelSize; y++)
        for (int x = 0; x < TilePixelSize; x++)
        {
            bool road = x is 7 or 8 || y is 7 or 8;
            if (road) WritePixel(pixels, TilePixelSize, x, y, 255, 220, 92, 225);
        }
        return pixels;
    }

    private static TileSet CreateTileSet() => new(
        "visual.world.tiles",
        new Vector2(256f, 256f),
        [
            new TileDefinition(new TileId(1), new SpriteRef("visual.world.grass")),
            new TileDefinition(new TileId(2), new SpriteRef("visual.world.sand")),
            new TileDefinition(new TileId(3), new SpriteRef("visual.world.stone")),
            new TileDefinition(new TileId(4), new SpriteRef("visual.world.water")),
            new TileDefinition(new TileId(5), new SpriteRef("visual.world.road"))
        ]);

    private static TileMap CreateMap(TileSetRef tileSet)
    {
        var map = new TileMap("visual.world", chunkWidth: 1, chunkHeight: 1);
        TileLayer ground = map.AddLayer("ground", tileSet, depth: -10);
        TileLayer roads = map.AddLayer("roads", tileSet, depth: 10);
        for (int y = 0; y < WorldChunkCount; y++)
        for (int x = 0; x < WorldChunkCount; x++)
        {
            int tile = (x + y * 2) % 4 + 1;
            ground.SetCell(x, y, new TileCell(new TileId((ushort)tile)));
            if (x == 1 || y == 2)
                roads.SetCell(x, y, new TileCell(new TileId(5)));
        }
        return map;
    }

    private static void AddRasterLevel(
        ICollection<TileWorldRasterChunkData> target,
        int level,
        int maximumCoordinate)
    {
        for (int y = 0; y <= maximumCoordinate; y++)
        for (int x = 0; x <= maximumCoordinate; x++)
        {
            target.Add(new TileWorldRasterChunkData(
                new TileWorldChunkKey(level, x, y),
                [
                    CreateRasterLayer(level, x, y, layerIndex: 0),
                    CreateRasterLayer(level, x, y, layerIndex: 1)
                ]));
        }
    }

    private static TileWorldRasterLayerData CreateRasterLayer(
        int level,
        int chunkX,
        int chunkY,
        int layerIndex)
    {
        byte[] rgba = CreateSurfacePixels(
            RasterSize,
            RasterSize,
            RasterGutter,
            level,
            chunkX,
            chunkY,
            layerIndex,
            preview: false);
        int encodedSize = RasterSize + RasterGutter * 2;
        return new TileWorldRasterLayerData(
            layerIndex,
            RasterSize,
            RasterSize,
            RasterGutter,
            TileWorldRasterEncoding.WebpLossless,
            EncodeRgba(rgba, encodedSize, encodedSize));
    }

    private static byte[] CreateSurfacePixels(
        int width,
        int height,
        int gutter,
        int level,
        int chunkX,
        int chunkY,
        int layerIndex,
        bool preview)
    {
        int encodedWidth = width + gutter * 2;
        int encodedHeight = height + gutter * 2;
        var pixels = new byte[encodedWidth * encodedHeight * 4];
        int factor = 1 << level;
        for (int encodedY = 0; encodedY < encodedHeight; encodedY++)
        for (int encodedX = 0; encodedX < encodedWidth; encodedX++)
        {
            int x = Math.Clamp(encodedX - gutter, 0, width - 1);
            int y = Math.Clamp(encodedY - gutter, 0, height - 1);
            float nx = (chunkX * factor + (x + .5f) / width * factor) / WorldChunkCount;
            float ny = (chunkY * factor + (y + .5f) / height * factor) / WorldChunkCount;
            if (layerIndex == 0)
                WriteGround(pixels, encodedWidth, encodedX, encodedY, nx, ny, preview);
            else
                WriteRoads(pixels, encodedWidth, encodedX, encodedY, nx, ny);
        }
        return pixels;
    }

    private static void WriteGround(
        byte[] pixels,
        int stride,
        int x,
        int y,
        float nx,
        float ny,
        bool preview)
    {
        int region = ((int)(nx * 4f) + (int)(ny * 4f) * 2) & 3;
        (byte r, byte g, byte b) = region switch
        {
            0 => ((byte)48, (byte)142, (byte)88),
            1 => ((byte)192, (byte)151, (byte)68),
            2 => ((byte)82, (byte)105, (byte)127),
            _ => ((byte)66, (byte)126, (byte)96)
        };
        float river = .34f + MathF.Sin(nx * MathF.Tau * 1.35f) * .09f;
        if (MathF.Abs(ny - river) < .055f)
            (r, g, b) = (35, 104, 166);
        else if (!preview &&
                 (Fraction(nx * 8f) < .018f || Fraction(ny * 8f) < .018f))
            (r, g, b) = (Scale(r, .68f), Scale(g, .68f), Scale(b, .68f));
        if (preview)
            (r, g, b) = (Scale(r, .52f), Scale(g, .52f), Scale(b, .58f));
        WritePixel(pixels, stride, x, y, r, g, b, 255);
    }

    private static void WriteRoads(
        byte[] pixels,
        int stride,
        int x,
        int y,
        float nx,
        float ny)
    {
        bool road = MathF.Abs(nx - .375f) < .012f || MathF.Abs(ny - .625f) < .012f;
        float dx = nx - .76f;
        float dy = ny - .22f;
        bool landmark = dx * dx + dy * dy < .0022f;
        if (road) WritePixel(pixels, stride, x, y, 255, 218, 88, 220);
        else if (landmark) WritePixel(pixels, stride, x, y, 246, 92, 72, 235);
    }

    private static byte[] EncodeRgba(byte[] rgba, int width, int height)
    {
        using var destination = new MemoryStream();
        var config = new WebPEncoderConfig()
            .SetLosslessPreset(9)
            .SetExact();
        WebPEncoder.Encode(
            rgba,
            width,
            height,
            checked(width * 4),
            WebPPixelFormat.Rgba,
            config,
            destination);
        return destination.ToArray();
    }

    private static void WritePixel(
        byte[] pixels,
        int width,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        int offset = (y * width + x) * 4;
        pixels[offset] = r;
        pixels[offset + 1] = g;
        pixels[offset + 2] = b;
        pixels[offset + 3] = a;
    }

    private static byte Scale(byte value, float scale) =>
        (byte)Math.Clamp((int)MathF.Round(value * scale), 0, 255);

    private static float Fraction(float value) => value - MathF.Floor(value);
}
