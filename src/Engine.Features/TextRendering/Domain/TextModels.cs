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
    internal ReadOnlySpan<FontRef> FontSpan => _fonts;
}

/// <summary>A logical draw request. It contains no graphics API or GPU handle.</summary>
public readonly record struct TextDrawCommand(
    FontFamily Fonts,
    string Text,
    Vector2 Position,
    float PixelSize,
    Vector4 Color)
{
    public TextLayoutOptions Layout { get; init; }
}

public enum TextWrapMode
{
    NoWrap,
    Character,
    Word
}

public enum TextAlignment
{
    Left,
    Center,
    Right
}

public enum TextOverflow
{
    Clip,
    Ellipsis
}

/// <summary>
/// Logical multi-line layout policy. MaxWidth and MaxLines use zero for unconstrained.
/// Automatic wrapping requires a positive MaxWidth.
/// </summary>
public readonly record struct TextLayoutOptions(
    float MaxWidth = 0f,
    TextWrapMode WrapMode = TextWrapMode.NoWrap,
    TextAlignment Alignment = TextAlignment.Left,
    int MaxLines = 0,
    TextOverflow Overflow = TextOverflow.Clip,
    float LineSpacing = 0f);

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

public readonly record struct TextLine(
    int GlyphStart,
    int GlyphCount,
    int TextStart,
    int TextLength,
    float Width,
    float Top,
    float Baseline,
    float Height);

public class TextLayout
{
    internal TextLayout(
        string text,
        FontFamily fonts,
        float pixelSize,
        TextLayoutOptions options,
        float width,
        float height,
        GlyphPlacement[] glyphs,
        int[] clusterStarts,
        TextLine[] lines,
        bool isTruncated)
    {
        Text = text;
        Fonts = fonts;
        PixelSize = pixelSize;
        Options = options;
        Width = width;
        Height = height;
        Glyphs = glyphs;
        ClusterStarts = clusterStarts;
        Lines = lines;
        IsTruncated = isTruncated;
    }

    public string Text { get; }
    public FontFamily Fonts { get; }
    public float PixelSize { get; }
    public TextLayoutOptions Options { get; }
    public float Width { get; }
    public float Height { get; }
    public float Baseline => Lines.Count == 0 ? 0f : Lines[0].Baseline;
    public IReadOnlyList<GlyphPlacement> Glyphs { get; }
    public IReadOnlyList<int> ClusterStarts { get; }
    public IReadOnlyList<TextLine> Lines { get; }
    public bool IsTruncated { get; }
}

public sealed class SingleLineTextLayout : TextLayout
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
        : base(
            text,
            fonts,
            pixelSize,
            default,
            width,
            height,
            glyphs,
            clusterStarts,
            [new TextLine(0, glyphs.Length, 0, text.Length, width, 0f, baseline, height)],
            false)
    {
    }
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
    internal PreparedTextLayout(TextLayout layout, PreparedGlyph[] glyphs)
    {
        Layout = layout;
        Glyphs = glyphs;
    }

    public TextLayout Layout { get; }
    public IReadOnlyList<PreparedGlyph> Glyphs { get; }
}

/// <summary>
/// Caller-owned reusable storage for dynamic text. Capacity grows geometrically and settled
/// LayoutInto calls allocate no managed memory.
/// </summary>
public sealed class TextLayoutBuffer
{
    internal GlyphPlacement[] ScratchGlyphs = [];
    internal TextClusterScratch[] ScratchClusters = [];
    internal TextLinePlan[] ScratchLines = [];
    internal GlyphPlacement[] OutputGlyphs = [];
    internal int[] OutputClusterStarts = [];
    internal TextLine[] OutputLines = [];

    public string Text { get; internal set; } = string.Empty;
    public FontFamily? Fonts { get; internal set; }
    public float PixelSize { get; internal set; }
    public TextLayoutOptions Options { get; internal set; }
    public float Width { get; internal set; }
    public float Height { get; internal set; }
    public bool IsTruncated { get; internal set; }
    public int GlyphCount { get; internal set; }
    public int ClusterCount { get; internal set; }
    public int LineCount { get; internal set; }
    public int ExpansionCount { get; internal set; }
    public ulong Revision { get; internal set; }
    public ReadOnlySpan<GlyphPlacement> Glyphs => OutputGlyphs.AsSpan(0, GlyphCount);
    public ReadOnlySpan<int> ClusterStarts => OutputClusterStarts.AsSpan(0, ClusterCount);
    public ReadOnlySpan<TextLine> Lines => OutputLines.AsSpan(0, LineCount);

    internal void EnsureScratchGlyphs(int required) =>
        Ensure(ref ScratchGlyphs, required);
    internal void EnsureScratchClusters(int required) =>
        Ensure(ref ScratchClusters, required);
    internal void EnsureScratchLines(int required) =>
        Ensure(ref ScratchLines, required);
    internal void EnsureOutputGlyphs(int required) =>
        Ensure(ref OutputGlyphs, required);
    internal void EnsureOutputClusters(int required) =>
        Ensure(ref OutputClusterStarts, required);
    internal void EnsureOutputLines(int required) =>
        Ensure(ref OutputLines, required);

    private void Ensure<T>(ref T[] values, int required)
    {
        if (required <= values.Length) return;
        int capacity = Math.Max(required, values.Length == 0 ? 8 : checked(values.Length * 2));
        Array.Resize(ref values, capacity);
        ExpansionCount++;
    }
}

/// <summary>Caller-owned atlas-resolved glyph storage paired with one TextLayoutBuffer.</summary>
public sealed class PreparedTextLayoutBuffer
{
    internal PreparedGlyph[] Items = [];
    public TextLayoutBuffer? Layout { get; internal set; }
    public int GlyphCount { get; internal set; }
    public int ExpansionCount { get; internal set; }
    internal ulong LayoutRevision { get; set; }
    public ReadOnlySpan<PreparedGlyph> Glyphs => Items.AsSpan(0, GlyphCount);

    internal void EnsureCapacity(int required)
    {
        if (required <= Items.Length) return;
        int capacity = Math.Max(required, Items.Length == 0 ? 8 : checked(Items.Length * 2));
        Array.Resize(ref Items, capacity);
        ExpansionCount++;
    }
}

internal struct TextClusterScratch
{
    public int TextStart;
    public int TextLength;
    public int GlyphStart;
    public int GlyphCount;
    public float Advance;
    public Rune FirstRune;
    public Rune LastRune;
    public bool IsWhitespace;
    public bool IsMandatoryBreak;
}

internal struct TextLinePlan
{
    public int ClusterStart;
    public int ClusterEnd;
    public int TextStart;
    public int TextEnd;
    public float Width;
    public bool AppendEllipsis;
}
