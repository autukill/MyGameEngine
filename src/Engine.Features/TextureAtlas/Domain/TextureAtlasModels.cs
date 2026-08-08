namespace GameEngine.Features.TextureAtlas.Domain;

using GameEngine.Core.Domain.Graphics;

public readonly record struct AtlasBuildOptions(
    int MaxPageWidth,
    int MaxPageHeight,
    int Padding = 1,
    int Extrude = 1)
{
    public static AtlasBuildOptions Default => new(2048, 2048);
}

/// <summary>A normalized, frame-sized unpremultiplied RGBA8 image.</summary>
public sealed record AtlasSourceFrame(
    string Key,
    int Width,
    int Height,
    ReadOnlyMemory<byte> RgbaPixels);

public readonly record struct AtlasFramePlacement(
    string Key,
    int PageIndex,
    PixelRectI SourceRect);

public sealed record AtlasPage(
    int Width,
    int Height,
    byte[] RgbaPixels);

public sealed class TextureAtlasBuildResult
{
    public TextureAtlasBuildResult(
        IEnumerable<AtlasPage> pages,
        IEnumerable<AtlasFramePlacement> placements,
        IEnumerable<string> passthroughKeys)
    {
        Pages = pages.ToArray();
        Placements = placements.ToDictionary(item => item.Key, StringComparer.Ordinal);
        PassthroughKeys = passthroughKeys.ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<AtlasPage> Pages { get; }
    public IReadOnlyDictionary<string, AtlasFramePlacement> Placements { get; }
    public IReadOnlySet<string> PassthroughKeys { get; }
}
