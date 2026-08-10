namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Globalization;
using System.Numerics;
using System.Text;
using GameEngine.Features.TextRendering.Domain;

/// <summary>Unicode-scalar single-line layout that preserves extended grapheme cluster boundaries.</summary>
public sealed class SingleLineTextLayouter(FontLibrary fonts)
{
    private readonly FontLibrary _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));

    public SingleLineTextLayout Layout(FontFamily family, string text, float pixelSize)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(pixelSize) || pixelSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (text.AsSpan().ContainsAny('\r', '\n'))
            throw new ArgumentException("Single-line text cannot contain line breaks.", nameof(text));

        int[] clusters = StringInfo.ParseCombiningCharacters(text);
        var placements = new List<GlyphPlacement>(text.Length);
        FontMetadata primaryMetadata = _fonts.GetMetadata(family.Primary);
        float baseline = primaryMetadata.AscentEm * pixelSize;
        float penX = 0f;
        FontRef previousFont = default;
        uint previousGlyph = 0;
        bool hasPrevious = false;

        for (int clusterNumber = 0; clusterNumber < clusters.Length; clusterNumber++)
        {
            int clusterStart = clusters[clusterNumber];
            int clusterEnd = clusterNumber + 1 < clusters.Length ? clusters[clusterNumber + 1] : text.Length;
            int clusterLength = clusterEnd - clusterStart;
            ReadOnlySpan<char> remaining = text.AsSpan(clusterStart, clusterLength);
            int runeOffset = 0;

            foreach (Rune rune in remaining.EnumerateRunes())
            {
                (FontRef font, uint glyphIndex) = _fonts.ResolveGlyph(family, rune);
                IGlyphRasterizer rasterizer = _fonts.GetRasterizer(font);
                if (hasPrevious && previousFont == font)
                    penX += ValidateFinite(rasterizer.MeasureKerning(previousGlyph, glyphIndex, pixelSize), "kerning");

                GlyphMetrics metrics = ValidateMetrics(rasterizer.MeasureGlyph(glyphIndex, pixelSize));
                var position = new Vector2(penX + metrics.BearingX, baseline - metrics.BearingY);
                placements.Add(new GlyphPlacement(
                    font,
                    glyphIndex,
                    rune,
                    clusterStart,
                    clusterLength,
                    position,
                    metrics));
                penX += metrics.Advance;
                previousFont = font;
                previousGlyph = glyphIndex;
                hasPrevious = true;
                runeOffset += rune.Utf16SequenceLength;
            }

            if (runeOffset != clusterLength)
                throw new InvalidOperationException("The text contains invalid UTF-16 data.");
        }

        float height = (primaryMetadata.AscentEm + primaryMetadata.DescentEm + primaryMetadata.LineGapEm) * pixelSize;
        return new SingleLineTextLayout(
            text,
            family,
            pixelSize,
            penX,
            height,
            baseline,
            placements.ToArray(),
            clusters);
    }

    public SingleLineTextLayout Layout(in TextDrawCommand command)
    {
        if (command.Fonts is null) throw new ArgumentException("A font family is required.", nameof(command));
        return Layout(command.Fonts, command.Text, command.PixelSize);
    }

    private static GlyphMetrics ValidateMetrics(GlyphMetrics metrics)
    {
        ValidateFinite(metrics.Advance, "glyph advance");
        ValidateFinite(metrics.BearingX, "glyph X bearing");
        ValidateFinite(metrics.BearingY, "glyph Y bearing");
        if (metrics.Advance < 0) throw new InvalidOperationException("Glyph advance cannot be negative.");
        if (metrics.Width < 0 || metrics.Height < 0)
            throw new InvalidOperationException("Glyph dimensions cannot be negative.");
        return metrics;
    }

    private static float ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException($"The rasterizer returned non-finite {name}.");
        return value;
    }
}
