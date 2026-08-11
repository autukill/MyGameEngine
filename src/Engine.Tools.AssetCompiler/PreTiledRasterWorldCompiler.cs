namespace GameEngine.Tools.AssetCompiler;

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using SkiaSharp;

internal sealed record PreTiledRasterWorldSource(
    string Name,
    PixelSizeI ChunkWorldSize,
    string ChunkPattern,
    string LayerName,
    int LayerDepth);

internal static class PreTiledRasterWorldCompiler
{
    private sealed class Document
    {
        public int SchemaVersion { get; init; }
        public string? Name { get; init; }
        public SizeDto? ChunkWorldSize { get; init; }
        public string? ChunkPattern { get; init; }
        public LayerDto? Layer { get; init; }
    }

    private sealed class SizeDto
    {
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private sealed class LayerDto
    {
        public string? Name { get; init; }
        public int Depth { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static bool IsSourcePath(string path) =>
        path.EndsWith(".pretiledworld.json", StringComparison.OrdinalIgnoreCase);

    public static PreTiledRasterWorldSource Parse(string path)
    {
        using var stream = File.OpenRead(path);
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
                ?? throw new InvalidDataException("Pre-tiled Raster world source is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Pre-tiled Raster world source is invalid JSON.", exception);
        }
        if (document.SchemaVersion != 1)
            throw new InvalidDataException(
                $"Unsupported pre-tiled Raster world schemaVersion '{document.SchemaVersion}'. Expected 1.");
        if (string.IsNullOrWhiteSpace(document.Name))
            throw new InvalidDataException("Pre-tiled Raster world name cannot be empty.");
        if (document.ChunkWorldSize is not { Width: > 0, Height: > 0 } size)
            throw new InvalidDataException("Pre-tiled Raster world chunkWorldSize must be positive.");
        if (string.IsNullOrWhiteSpace(document.ChunkPattern) ||
            !document.ChunkPattern.Contains("{row}", StringComparison.Ordinal) ||
            !document.ChunkPattern.Contains("{column}", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Pre-tiled Raster world chunkPattern must contain {row} and {column}.");
        if (document.Layer is null || string.IsNullOrWhiteSpace(document.Layer.Name))
            throw new InvalidDataException("Pre-tiled Raster world layer name cannot be empty.");
        return new PreTiledRasterWorldSource(
            document.Name,
            new PixelSizeI(size.Width, size.Height),
            document.ChunkPattern,
            document.Layer.Name,
            document.Layer.Depth);
    }

    public static IEnumerable<string> EnumerateChunkRelativePaths(
        PreTiledRasterWorldSource source,
        TileWorldChunkBounds bounds)
    {
        for (int row = bounds.MinY; row <= bounds.MaxY; row++)
            for (int column = bounds.MinX; column <= bounds.MaxX; column++)
                yield return source.ChunkPattern
                    .Replace("{row}", row.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal)
                    .Replace("{column}", column.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
    }

    public static TileWorldArchiveBuild Compile(
        PreTiledRasterWorldSource source,
        TileWorldAssetBuildDefinition build,
        Func<string, string> resolveSource,
        IImageDecoder decoder,
        IReadOnlyList<TileWorldFallbackSurfaceData> fallbackSurfaces)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(resolveSource);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(fallbackSurfaces);
        if (build.Gutter != 0)
            throw new InvalidDataException("Pre-tiled Raster worlds currently require gutter 0.");

        var metadata = new TileWorldMetadata(
            source.Name,
            1,
            1,
            new Vector2(source.ChunkWorldSize.Width, source.ChunkWorldSize.Height),
            build.Bounds,
            build.LodCount,
            new TileWorldRasterSettings(
                build.RasterChunkSize.Width,
                build.RasterChunkSize.Height,
                0,
                build.Sampling == TextureSampler.PixelArt
                    ? TileWorldRasterSampling.PixelArt
                    : TileWorldRasterSampling.Smooth),
            [new TileWorldLayerMetadata(
                source.LayerName,
                new TileSetRef("__raster-only"),
                source.LayerDepth,
                Vector2.Zero,
                true)],
            fallbackSurfaces.Select(item => item.Metadata));

        var allChunks = new List<TileWorldRasterChunkData>();
        var previous = new Dictionary<TileWorldChunkKey, byte[]>();
        string[] relativePaths = EnumerateChunkRelativePaths(source, build.Bounds).ToArray();
        int relativeIndex = 0;
        for (int row = build.Bounds.MinY; row <= build.Bounds.MaxY; row++)
        {
            for (int column = build.Bounds.MinX; column <= build.Bounds.MaxX; column++)
            {
                string path = resolveSource(relativePaths[relativeIndex++]);
                byte[] encoded = File.ReadAllBytes(path);
                ValidateWebpChunk(path, encoded, decoder, metadata.RasterSettings);
                var key = new TileWorldChunkKey(0, column, row);
                previous.Add(key, encoded);
                allChunks.Add(CreateChunk(key, encoded, TileWorldRasterEncoding.Webp, metadata.RasterSettings));
            }
        }

        for (int level = 1; level < metadata.DeclaredLodCount; level++)
        {
            TileWorldChunkBounds levelBounds = metadata.GetChunkBounds(level);
            var next = new Dictionary<TileWorldChunkKey, byte[]>();
            for (int row = levelBounds.MinY; row <= levelBounds.MaxY; row++)
            {
                for (int column = levelBounds.MinX; column <= levelBounds.MaxX; column++)
                {
                    var key = new TileWorldChunkKey(level, column, row);
                    byte[] encoded = BuildParentChunk(
                        key,
                        previous,
                        metadata.RasterSettings,
                        build.Sampling);
                    next.Add(key, encoded);
                    allChunks.Add(CreateChunk(
                        key,
                        encoded,
                        TileWorldRasterEncoding.WebpLossless,
                        metadata.RasterSettings));
                }
            }
            previous = next;
        }

        return new TileWorldArchiveBuild(metadata, [], allChunks, fallbackSurfaces);
    }

    private static TileWorldRasterChunkData CreateChunk(
        TileWorldChunkKey key,
        byte[] bytes,
        TileWorldRasterEncoding encoding,
        TileWorldRasterSettings settings) =>
        new(key,
        [
            new TileWorldRasterLayerData(
                0,
                settings.Width,
                settings.Height,
                settings.Gutter,
                encoding,
                bytes)
        ]);

    private static void ValidateWebpChunk(
        string path,
        byte[] encoded,
        IImageDecoder decoder,
        TileWorldRasterSettings settings)
    {
        if (encoded.Length < 12 ||
            !encoded.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !encoded.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException($"Pre-tiled Raster Chunk '{path}' is not WebP.");
        using var stream = new MemoryStream(encoded, writable: false);
        DecodedImage image = decoder.Decode(stream);
        if (image.Width != settings.Width || image.Height != settings.Height)
            throw new InvalidDataException(
                $"Pre-tiled Raster Chunk '{path}' is {image.Width}x{image.Height}; " +
                $"expected {settings.Width}x{settings.Height}.");
    }

    private static byte[] BuildParentChunk(
        TileWorldChunkKey parent,
        IReadOnlyDictionary<TileWorldChunkKey, byte[]> children,
        TileWorldRasterSettings settings,
        TextureSampler sampling)
    {
        var info = new SKImageInfo(
            settings.Width,
            settings.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var target = new SKBitmap(info);
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = false };
        var samplingOptions = sampling == TextureSampler.PixelArt
            ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)
            : new SKSamplingOptions(SKCubicResampler.Mitchell);
        float halfWidth = settings.Width * 0.5f;
        float halfHeight = settings.Height * 0.5f;
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                var childKey = new TileWorldChunkKey(
                    parent.Level - 1,
                    checked(parent.X * 2 + x),
                    checked(parent.Y * 2 + y));
                if (!children.TryGetValue(childKey, out byte[]? encoded)) continue;
                using SKBitmap child = SKBitmap.Decode(encoded)
                    ?? throw new InvalidDataException($"Could not decode Raster Chunk '{childKey}'.");
                canvas.DrawBitmap(
                    child,
                    new SKRect(0f, 0f, child.Width, child.Height),
                    new SKRect(
                        x * halfWidth,
                        y * halfHeight,
                        (x + 1) * halfWidth,
                        (y + 1) * halfHeight),
                    samplingOptions,
                    paint);
            }
        }
        canvas.Flush();
        var pixels = new byte[checked(settings.Width * settings.Height * 4)];
        Marshal.Copy(target.GetPixels(), pixels, 0, pixels.Length);
        return TileWorldLosslessWebpEncoder.Encode(settings.Width, settings.Height, pixels);
    }
}
