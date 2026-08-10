namespace GameEngine.Features.TextRendering.Domain;

using System.Text;

/// <summary>Scale-independent font metrics. Em values are relative to the requested pixel size.</summary>
public readonly record struct FontMetadata(
    string FamilyName,
    int UnitsPerEm,
    float AscentEm,
    float DescentEm,
    float LineGapEm = 0f);

/// <summary>Metrics for one glyph at a concrete pixel size.</summary>
public readonly record struct GlyphMetrics(
    float Advance,
    float BearingX,
    float BearingY,
    int Width,
    int Height);

/// <summary>An 8-bit alpha bitmap. Rows are tightly packed from top to bottom.</summary>
public sealed class GlyphBitmap
{
    public GlyphBitmap(int width, int height, ReadOnlyMemory<byte> alphaPixels)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((width == 0) != (height == 0))
            throw new ArgumentException("Empty glyph bitmaps must have both dimensions set to zero.");
        if (alphaPixels.Length != checked(width * height))
            throw new ArgumentException("Glyph alpha data must contain exactly width * height bytes.", nameof(alphaPixels));

        Width = width;
        Height = height;
        AlphaPixels = alphaPixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> AlphaPixels { get; }
    public bool IsEmpty => Width == 0;

    public static GlyphBitmap Empty { get; } = new(0, 0, ReadOnlyMemory<byte>.Empty);
}

/// <summary>Injectable font backend. A SkiaSharp or FreeType adapter can implement this contract.</summary>
public interface IGlyphRasterizer
{
    uint MissingGlyphIndex { get; }
    bool TryGetGlyphIndex(Rune rune, out uint glyphIndex);
    GlyphMetrics MeasureGlyph(uint glyphIndex, float pixelSize);
    float MeasureKerning(uint leftGlyphIndex, uint rightGlyphIndex, float pixelSize);
    GlyphBitmap RasterizeGlyph(uint glyphIndex, float pixelSize);
}

public enum FontResourceOwnership
{
    Borrowed,
    Owned
}
