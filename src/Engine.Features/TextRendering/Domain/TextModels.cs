namespace GameEngine.Features.TextRendering.Domain;

using System.Numerics;
using System.Text;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>An immutable primary font plus ordered fallbacks.</summary>
public sealed class FontFamily
{
    private readonly FontRef[] _fonts;

    internal FontFamily(FontRef[] fonts)
    {
        _fonts = fonts;
        Fonts = Array.AsReadOnly(_fonts);
    }

    public IReadOnlyList<FontRef> Fonts { get; }
    public FontRef Primary => _fonts[0];
}

/// <summary>A logical draw request. It contains no graphics API or GPU handle.</summary>
public readonly record struct TextDrawCommand(
    FontFamily Fonts,
    string Text,
    Vector2 Position,
    float PixelSize,
    Vector4 Color);

/// <summary>
/// One positioned Unicode scalar. ClusterStart/ClusterLength identify the complete UTF-16 grapheme
/// cluster, so selection, truncation and animation can avoid splitting surrogate pairs or combining text.
/// </summary>
public readonly record struct GlyphPlacement(
    FontRef Font,
    uint GlyphIndex,
    Rune Rune,
    int ClusterStart,
    int ClusterLength,
    Vector2 Position,
    GlyphMetrics Metrics);

public sealed class SingleLineTextLayout
{
    internal SingleLineTextLayout(
        string text,
        FontFamily fonts,
        float pixelSize,
        float width,
        float height,
        float baseline,
        GlyphPlacement[] glyphs,
        int[] clusterStarts)
    {
        Text = text;
        Fonts = fonts;
        PixelSize = pixelSize;
        Width = width;
        Height = height;
        Baseline = baseline;
        Glyphs = glyphs;
        ClusterStarts = clusterStarts;
    }

    public string Text { get; }
    public FontFamily Fonts { get; }
    public float PixelSize { get; }
    public float Width { get; }
    public float Height { get; }
    public float Baseline { get; }
    public IReadOnlyList<GlyphPlacement> Glyphs { get; }
    public IReadOnlyList<int> ClusterStarts { get; }
}

public readonly record struct GlyphAtlasEntry(
    TextureRef Texture,
    PixelRectI SourceRect,
    Vector4 UvBounds,
    bool HasPixels);

public readonly record struct PreparedGlyph(
    GlyphPlacement Placement,
    GlyphAtlasEntry Atlas);

public sealed class PreparedTextLayout
{
    internal PreparedTextLayout(SingleLineTextLayout layout, PreparedGlyph[] glyphs)
    {
        Layout = layout;
        Glyphs = glyphs;
    }

    public SingleLineTextLayout Layout { get; }
    public IReadOnlyList<PreparedGlyph> Glyphs { get; }
}
