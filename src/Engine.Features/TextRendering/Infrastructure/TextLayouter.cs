namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Globalization;
using System.Numerics;
using System.Text;
using GameEngine.Features.TextRendering.Domain;

/// <summary>
/// Deterministic Unicode-scalar multi-line layout. Grapheme clusters are never split; Word mode
/// prefers whitespace and CJK boundaries, with a small explicit line-start/line-end punctuation set.
/// </summary>
public sealed class TextLayouter
{
    private static readonly Rune EllipsisRune = new(0x2026);
    private readonly FontLibrary _fonts;
    private long _layoutCount;
    private long _bufferLayoutCount;
    private long _missingGlyphCount;

    public TextLayouter(FontLibrary fonts) =>
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));

    public TextLayout Layout(
        FontFamily family,
        string text,
        float pixelSize,
        TextLayoutOptions options = default)
    {
        var buffer = new TextLayoutBuffer();
        LayoutInto(family, text, pixelSize, options, buffer);
        var glyphs = buffer.Glyphs.ToArray();
        var clusters = buffer.ClusterStarts.ToArray();
        var lines = buffer.Lines.ToArray();
        return new TextLayout(
            text,
            family,
            pixelSize,
            options,
            buffer.Width,
            buffer.Height,
            glyphs,
            clusters,
            lines,
            buffer.IsTruncated);
    }

    public void LayoutInto(
        FontFamily family,
        string text,
        float pixelSize,
        TextLayoutOptions options,
        TextLayoutBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(destination);
        Validate(pixelSize, options);
        _layoutCount++;
        _bufferLayoutCount++;

        destination.Text = text;
        destination.Fonts = family;
        destination.PixelSize = pixelSize;
        destination.Options = options;
        destination.GlyphCount = 0;
        destination.ClusterCount = 0;
        destination.LineCount = 0;
        destination.IsTruncated = false;
        destination.Revision = destination.Revision == ulong.MaxValue ? 1UL : destination.Revision + 1UL;

        ParseClusters(family, text, pixelSize, destination, out int scratchGlyphCount, out int scratchClusterCount);
        BuildLinePlans(text, pixelSize, options, destination, scratchClusterCount, out int lineCount);
        EmitLayout(family, text, pixelSize, options, destination, lineCount);
    }

    public TextLayouterDiagnostics CaptureDiagnostics() => new(
        _layoutCount,
        _bufferLayoutCount,
        _missingGlyphCount);

    private void ParseClusters(
        FontFamily family,
        string text,
        float pixelSize,
        TextLayoutBuffer buffer,
        out int glyphCount,
        out int clusterCount)
    {
        glyphCount = 0;
        clusterCount = 0;
        int offset = 0;
        while (offset < text.Length)
        {
            int newlineLength = text[offset] switch
            {
                '\r' when offset + 1 < text.Length && text[offset + 1] == '\n' => 2,
                '\r' or '\n' => 1,
                _ => 0
            };
            if (newlineLength > 0)
            {
                buffer.EnsureScratchClusters(clusterCount + 1);
                buffer.ScratchClusters[clusterCount++] = new TextClusterScratch
                {
                    TextStart = offset,
                    TextLength = newlineLength,
                    GlyphStart = glyphCount,
                    IsMandatoryBreak = true
                };
                offset += newlineLength;
                continue;
            }

            int clusterLength = StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            if (clusterLength <= 0)
                throw new InvalidOperationException("Unable to advance through the text element sequence.");
            int glyphStart = glyphCount;
            float advance = 0f;
            FontRef previousFont = default;
            uint previousGlyph = 0;
            bool hasPrevious = false;
            Rune firstRune = default;
            Rune lastRune = default;
            bool hasRune = false;
            bool whitespace = true;

            foreach (Rune rune in text.AsSpan(offset, clusterLength).EnumerateRunes())
            {
                (FontRef font, uint glyphIndex) = _fonts.ResolveGlyph(family, rune, out bool missing);
                if (missing) _missingGlyphCount++;
                IGlyphRasterizer rasterizer = _fonts.GetRasterizer(font);
                if (hasPrevious && previousFont == font)
                {
                    advance += ValidateFinite(
                        rasterizer.MeasureKerning(previousGlyph, glyphIndex, pixelSize),
                        "kerning");
                }
                GlyphMetrics metrics = ValidateMetrics(rasterizer.MeasureGlyph(glyphIndex, pixelSize));
                buffer.EnsureScratchGlyphs(glyphCount + 1);
                buffer.ScratchGlyphs[glyphCount++] = new GlyphPlacement(
                    font,
                    glyphIndex,
                    rune,
                    offset,
                    clusterLength,
                    new Vector2(advance + metrics.BearingX, -metrics.BearingY),
                    metrics);
                advance += metrics.Advance;
                if (!hasRune) firstRune = rune;
                lastRune = rune;
                hasRune = true;
                whitespace &= Rune.IsWhiteSpace(rune);
                previousFont = font;
                previousGlyph = glyphIndex;
                hasPrevious = true;
            }
            if (!hasRune) throw new InvalidOperationException("A grapheme cluster contained no Unicode scalar.");

            buffer.EnsureScratchClusters(clusterCount + 1);
            buffer.ScratchClusters[clusterCount++] = new TextClusterScratch
            {
                TextStart = offset,
                TextLength = clusterLength,
                GlyphStart = glyphStart,
                GlyphCount = glyphCount - glyphStart,
                Advance = advance,
                FirstRune = firstRune,
                LastRune = lastRune,
                IsWhitespace = whitespace
            };
            offset += clusterLength;
        }
    }

    private void BuildLinePlans(
        string text,
        float pixelSize,
        TextLayoutOptions options,
        TextLayoutBuffer buffer,
        int clusterCount,
        out int lineCount)
    {
        lineCount = 0;
        bool trailingBreak = false;
        int cursor = 0;
        while (cursor < clusterCount)
        {
            int segmentEnd = cursor;
            while (segmentEnd < clusterCount && !buffer.ScratchClusters[segmentEnd].IsMandatoryBreak)
                segmentEnd++;

            if (!TryAddSegmentLines(text, pixelSize, options, buffer, cursor, segmentEnd, ref lineCount))
                return;

            if (segmentEnd < clusterCount)
            {
                trailingBreak = segmentEnd == clusterCount - 1;
                cursor = segmentEnd + 1;
            }
            else
            {
                trailingBreak = false;
                cursor = segmentEnd;
            }
        }

        if (clusterCount == 0 || trailingBreak)
            TryAddEmptyLine(text.Length, options, buffer, ref lineCount);
    }

    private bool TryAddSegmentLines(
        string text,
        float pixelSize,
        TextLayoutOptions options,
        TextLayoutBuffer buffer,
        int start,
        int end,
        ref int lineCount)
    {
        if (start == end)
            return TryAddEmptyLine(
                start < buffer.ScratchClusters.Length ? buffer.ScratchClusters[start].TextStart : text.Length,
                options,
                buffer,
                ref lineCount);

        int cursor = start;
        while (cursor < end)
        {
            if (ReachedLineLimit(options, lineCount))
            {
                MarkLastLineTruncated(options, buffer, lineCount);
                return false;
            }

            int lineEnd;
            float width;
            bool horizontalTruncation = false;
            if (options.WrapMode == TextWrapMode.NoWrap)
            {
                lineEnd = end;
                width = MeasureRange(buffer, cursor, lineEnd);
                if (options.MaxWidth > 0f && width > options.MaxWidth)
                {
                    lineEnd = FindFittingPrefix(buffer, cursor, end, options.MaxWidth);
                    width = MeasureRange(buffer, cursor, lineEnd);
                    horizontalTruncation = true;
                }
            }
            else
            {
                lineEnd = FindWrappedEnd(buffer, cursor, end, options, out width);
            }

            buffer.EnsureScratchLines(lineCount + 1);
            int textStart = buffer.ScratchClusters[cursor].TextStart;
            int textEnd = lineEnd > cursor
                ? buffer.ScratchClusters[lineEnd - 1].TextStart + buffer.ScratchClusters[lineEnd - 1].TextLength
                : textStart;
            buffer.ScratchLines[lineCount++] = new TextLinePlan
            {
                ClusterStart = cursor,
                ClusterEnd = lineEnd,
                TextStart = textStart,
                TextEnd = textEnd,
                Width = width,
                AppendEllipsis = horizontalTruncation && options.Overflow == TextOverflow.Ellipsis
            };
            if (horizontalTruncation)
            {
                buffer.IsTruncated = true;
                return true;
            }
            cursor = lineEnd;
        }
        return true;
    }

    private bool TryAddEmptyLine(
        int textOffset,
        TextLayoutOptions options,
        TextLayoutBuffer buffer,
        ref int lineCount)
    {
        if (ReachedLineLimit(options, lineCount))
        {
            MarkLastLineTruncated(options, buffer, lineCount);
            return false;
        }
        buffer.EnsureScratchLines(lineCount + 1);
        buffer.ScratchLines[lineCount++] = new TextLinePlan
        {
            ClusterStart = 0,
            ClusterEnd = 0,
            TextStart = textOffset,
            TextEnd = textOffset
        };
        return true;
    }

    private static bool ReachedLineLimit(TextLayoutOptions options, int lineCount) =>
        options.MaxLines > 0 && lineCount >= options.MaxLines;

    private static void MarkLastLineTruncated(
        TextLayoutOptions options,
        TextLayoutBuffer buffer,
        int lineCount)
    {
        buffer.IsTruncated = true;
        if (lineCount > 0 && options.Overflow == TextOverflow.Ellipsis)
            buffer.ScratchLines[lineCount - 1].AppendEllipsis = true;
    }

    private int FindWrappedEnd(
        TextLayoutBuffer buffer,
        int start,
        int end,
        TextLayoutOptions options,
        out float width)
    {
        int lastLegalBreak = -1;
        float lastLegalWidth = 0f;
        float acceptedWidth = 0f;
        FontRef previousFont = default;
        uint previousGlyph = 0;
        bool hasPrevious = false;
        for (int current = start; current < end; current++)
        {
            ref TextClusterScratch cluster = ref buffer.ScratchClusters[current];
            GlyphPlacement first = buffer.ScratchGlyphs[cluster.GlyphStart];
            float crossKerning = hasPrevious && previousFont == first.Font
                ? MeasureKerning(first.Font, previousGlyph, first.GlyphIndex, buffer.PixelSize)
                : 0f;
            float candidateWidth = acceptedWidth + crossKerning + cluster.Advance;
            if (candidateWidth > options.MaxWidth)
            {
                if (lastLegalBreak > start)
                {
                    width = lastLegalWidth;
                    return lastLegalBreak;
                }
                bool breakBeforeCurrentIsForbidden = current > start &&
                    !CanBreakBetween(buffer, current - 1, current, options.WrapMode);
                if (breakBeforeCurrentIsForbidden)
                {
                    // A short punctuation run may intentionally exceed MaxWidth rather than
                    // creating a line that begins with closing punctuation.
                    if (current + 1 == end)
                    {
                        width = candidateWidth;
                        return end;
                    }
                    acceptedWidth = candidateWidth;
                    GlyphPlacement continuedLast =
                        buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
                    previousFont = continuedLast.Font;
                    previousGlyph = continuedLast.GlyphIndex;
                    hasPrevious = true;
                    continue;
                }
                if (current == start)
                {
                    width = candidateWidth;
                    return current + 1;
                }
                width = MeasureRange(buffer, start, current);
                return current;
            }

            acceptedWidth = candidateWidth;
            GlyphPlacement last = buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
            previousFont = last.Font;
            previousGlyph = last.GlyphIndex;
            hasPrevious = true;

            int boundary = current + 1;
            if (boundary == end || CanBreakBetween(buffer, current, boundary, options.WrapMode))
            {
                lastLegalBreak = boundary;
                lastLegalWidth = candidateWidth;
            }
        }
        width = acceptedWidth;
        return end;
    }

    private int FindFittingPrefix(TextLayoutBuffer buffer, int start, int end, float maxWidth)
    {
        int result = start;
        float width = 0f;
        FontRef previousFont = default;
        uint previousGlyph = 0;
        bool hasPrevious = false;
        for (int current = start; current < end; current++)
        {
            ref TextClusterScratch cluster = ref buffer.ScratchClusters[current];
            GlyphPlacement first = buffer.ScratchGlyphs[cluster.GlyphStart];
            float crossKerning = hasPrevious && previousFont == first.Font
                ? MeasureKerning(first.Font, previousGlyph, first.GlyphIndex, buffer.PixelSize)
                : 0f;
            float candidate = width + crossKerning + cluster.Advance;
            if (candidate > maxWidth) break;
            width = candidate;
            result = current + 1;
            GlyphPlacement last = buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
            previousFont = last.Font;
            previousGlyph = last.GlyphIndex;
            hasPrevious = true;
        }
        return result;
    }

    private float MeasureRange(TextLayoutBuffer buffer, int start, int end)
    {
        float width = 0f;
        FontRef previousFont = default;
        uint previousGlyph = 0;
        bool hasPrevious = false;
        for (int clusterIndex = start; clusterIndex < end; clusterIndex++)
        {
            ref TextClusterScratch cluster = ref buffer.ScratchClusters[clusterIndex];
            if (cluster.IsMandatoryBreak || cluster.GlyphCount == 0) continue;
            GlyphPlacement first = buffer.ScratchGlyphs[cluster.GlyphStart];
            if (hasPrevious && previousFont == first.Font)
            {
                width += ValidateFinite(
                    _fonts.GetRasterizer(first.Font).MeasureKerning(
                        previousGlyph,
                        first.GlyphIndex,
                        buffer.PixelSize),
                    "kerning");
            }
            width += cluster.Advance;
            GlyphPlacement last = buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
            previousFont = last.Font;
            previousGlyph = last.GlyphIndex;
            hasPrevious = true;
        }
        return width;
    }

    private void EmitLayout(
        FontFamily family,
        string text,
        float pixelSize,
        TextLayoutOptions options,
        TextLayoutBuffer buffer,
        int lineCount)
    {
        FontMetadata metadata = _fonts.GetMetadata(family.Primary);
        float lineHeight = (metadata.AscentEm + metadata.DescentEm + metadata.LineGapEm) * pixelSize;
        float stride = lineHeight + options.LineSpacing;
        float maxLineWidth = 0f;

        GlyphPlacement ellipsis = default;
        float ellipsisAdvance = 0f;
        bool needsEllipsis = false;
        for (int i = 0; i < lineCount; i++) needsEllipsis |= buffer.ScratchLines[i].AppendEllipsis;
        if (needsEllipsis)
            ellipsis = MeasureEllipsis(family, text.Length, pixelSize, out ellipsisAdvance);

        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            ref TextLinePlan plan = ref buffer.ScratchLines[lineIndex];
            if (plan.AppendEllipsis)
            {
                float limit = options.MaxWidth;
                float prefixWidth = MeasureRange(buffer, plan.ClusterStart, plan.ClusterEnd);
                while (plan.ClusterEnd > plan.ClusterStart && limit > 0f &&
                       MeasureWithEllipsis(
                           buffer,
                           plan.ClusterStart,
                           plan.ClusterEnd,
                           prefixWidth,
                           ellipsis,
                           ellipsisAdvance) > limit)
                {
                    int removed = plan.ClusterEnd - 1;
                    ref TextClusterScratch cluster = ref buffer.ScratchClusters[removed];
                    prefixWidth -= cluster.Advance;
                    if (removed > plan.ClusterStart)
                    {
                        GlyphPlacement previous = LastGlyph(buffer, removed - 1);
                        GlyphPlacement first = buffer.ScratchGlyphs[cluster.GlyphStart];
                        if (previous.Font == first.Font)
                        {
                            prefixWidth -= MeasureKerning(
                                first.Font,
                                previous.GlyphIndex,
                                first.GlyphIndex,
                                pixelSize);
                        }
                    }
                    plan.ClusterEnd--;
                }
                plan.Width = MeasureWithEllipsis(
                    buffer,
                    plan.ClusterStart,
                    plan.ClusterEnd,
                    prefixWidth,
                    ellipsis,
                    ellipsisAdvance);
                plan.TextEnd = plan.ClusterEnd > plan.ClusterStart
                    ? buffer.ScratchClusters[plan.ClusterEnd - 1].TextStart +
                      buffer.ScratchClusters[plan.ClusterEnd - 1].TextLength
                    : plan.TextStart;
            }
            maxLineWidth = MathF.Max(maxLineWidth, plan.Width);
        }

        float boxWidth = options.MaxWidth > 0f ? options.MaxWidth : maxLineWidth;
        int outputGlyphCount = 0;
        int outputClusterCount = 0;
        for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            ref TextLinePlan plan = ref buffer.ScratchLines[lineIndex];
            float xOffset = options.Alignment switch
            {
                TextAlignment.Left => 0f,
                TextAlignment.Center => MathF.Max(0f, (boxWidth - plan.Width) * .5f),
                TextAlignment.Right => MathF.Max(0f, boxWidth - plan.Width),
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };
            float top = lineIndex * stride;
            float baseline = top + metadata.AscentEm * pixelSize;
            float penX = 0f;
            FontRef previousFont = default;
            uint previousGlyph = 0;
            bool hasPrevious = false;
            int lineGlyphStart = outputGlyphCount;

            for (int clusterIndex = plan.ClusterStart; clusterIndex < plan.ClusterEnd; clusterIndex++)
            {
                ref TextClusterScratch cluster = ref buffer.ScratchClusters[clusterIndex];
                if (cluster.GlyphCount == 0) continue;
                GlyphPlacement first = buffer.ScratchGlyphs[cluster.GlyphStart];
                float crossKerning = 0f;
                if (hasPrevious && previousFont == first.Font)
                {
                    crossKerning = ValidateFinite(
                        _fonts.GetRasterizer(first.Font).MeasureKerning(
                            previousGlyph,
                            first.GlyphIndex,
                            pixelSize),
                        "kerning");
                }
                buffer.EnsureOutputClusters(outputClusterCount + 1);
                buffer.OutputClusterStarts[outputClusterCount++] = cluster.TextStart;
                buffer.EnsureOutputGlyphs(outputGlyphCount + cluster.GlyphCount);
                for (int glyphOffset = 0; glyphOffset < cluster.GlyphCount; glyphOffset++)
                {
                    GlyphPlacement source = buffer.ScratchGlyphs[cluster.GlyphStart + glyphOffset];
                    buffer.OutputGlyphs[outputGlyphCount++] = source with
                    {
                        Position = new Vector2(
                            xOffset + penX + crossKerning + source.Position.X,
                            baseline + source.Position.Y)
                    };
                }
                penX += crossKerning + cluster.Advance;
                GlyphPlacement last = buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
                previousFont = last.Font;
                previousGlyph = last.GlyphIndex;
                hasPrevious = true;
            }

            if (plan.AppendEllipsis)
            {
                float crossKerning = hasPrevious && previousFont == ellipsis.Font
                    ? ValidateFinite(
                        _fonts.GetRasterizer(ellipsis.Font).MeasureKerning(
                            previousGlyph,
                            ellipsis.GlyphIndex,
                            pixelSize),
                        "kerning")
                    : 0f;
                buffer.EnsureOutputGlyphs(outputGlyphCount + 1);
                buffer.OutputGlyphs[outputGlyphCount++] = ellipsis with
                {
                    Position = new Vector2(
                        xOffset + penX + crossKerning + ellipsis.Metrics.BearingX,
                        baseline - ellipsis.Metrics.BearingY)
                };
            }

            buffer.EnsureOutputLines(lineIndex + 1);
            buffer.OutputLines[lineIndex] = new TextLine(
                lineGlyphStart,
                outputGlyphCount - lineGlyphStart,
                plan.TextStart,
                Math.Max(0, plan.TextEnd - plan.TextStart),
                plan.Width,
                top,
                baseline,
                lineHeight);
        }

        buffer.GlyphCount = outputGlyphCount;
        buffer.ClusterCount = outputClusterCount;
        buffer.LineCount = lineCount;
        buffer.Width = boxWidth;
        buffer.Height = lineCount == 0 ? 0f : lineHeight * lineCount + options.LineSpacing * (lineCount - 1);
    }

    private GlyphPlacement MeasureEllipsis(
        FontFamily family,
        int clusterStart,
        float pixelSize,
        out float advance)
    {
        (FontRef font, uint glyphIndex) = _fonts.ResolveGlyph(family, EllipsisRune, out bool missing);
        if (missing) _missingGlyphCount++;
        GlyphMetrics metrics = ValidateMetrics(_fonts.GetRasterizer(font).MeasureGlyph(glyphIndex, pixelSize));
        advance = metrics.Advance;
        return new GlyphPlacement(
            font,
            glyphIndex,
            EllipsisRune,
            clusterStart,
            0,
            Vector2.Zero,
            metrics);
    }

    private float MeasureWithEllipsis(
        TextLayoutBuffer buffer,
        int start,
        int end,
        float prefixWidth,
        in GlyphPlacement ellipsis,
        float ellipsisAdvance)
    {
        if (end <= start) return ellipsisAdvance;
        GlyphPlacement previous = LastGlyph(buffer, end - 1);
        float crossKerning = previous.Font == ellipsis.Font
            ? MeasureKerning(
                ellipsis.Font,
                previous.GlyphIndex,
                ellipsis.GlyphIndex,
                buffer.PixelSize)
            : 0f;
        return prefixWidth + crossKerning + ellipsisAdvance;
    }

    private static GlyphPlacement LastGlyph(TextLayoutBuffer buffer, int clusterIndex)
    {
        ref TextClusterScratch cluster = ref buffer.ScratchClusters[clusterIndex];
        return buffer.ScratchGlyphs[cluster.GlyphStart + cluster.GlyphCount - 1];
    }

    private float MeasureKerning(FontRef font, uint left, uint right, float pixelSize) =>
        ValidateFinite(
            _fonts.GetRasterizer(font).MeasureKerning(left, right, pixelSize),
            "kerning");

    private static bool CanBreakBetween(
        TextLayoutBuffer buffer,
        int leftIndex,
        int rightIndex,
        TextWrapMode mode)
    {
        ref TextClusterScratch left = ref buffer.ScratchClusters[leftIndex];
        ref TextClusterScratch right = ref buffer.ScratchClusters[rightIndex];
        if (IsForbiddenLineEnd(left.LastRune) || IsForbiddenLineStart(right.FirstRune)) return false;
        if (mode == TextWrapMode.Character) return true;
        return left.IsWhitespace || IsCjk(left.LastRune) || IsCjk(right.FirstRune);
    }

    private static bool IsForbiddenLineStart(Rune rune) =>
        rune.Value is 0x3001 or 0x3002 or 0xFF0C or 0xFF0E or 0xFF1B or 0xFF1A or
            0xFF01 or 0xFF1F or 0xFF09 or 0x3011 or 0x300B or 0x300D or 0x300F or
            0x201D or 0x2019 or 0x2026;

    private static bool IsForbiddenLineEnd(Rune rune) =>
        rune.Value is 0xFF08 or 0x3010 or 0x300A or 0x300C or 0x300E or 0x201C or 0x2018;

    private static bool IsCjk(Rune rune) => rune.Value is
        >= 0x2E80 and <= 0x9FFF or
        >= 0xF900 and <= 0xFAFF or
        >= 0x20000 and <= 0x3134F;

    private static void Validate(float pixelSize, TextLayoutOptions options)
    {
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (!float.IsFinite(options.MaxWidth) || options.MaxWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxWidth must be finite and non-negative.");
        if (!float.IsFinite(options.LineSpacing) || options.LineSpacing < 0f)
            throw new ArgumentOutOfRangeException(nameof(options), "LineSpacing must be finite and non-negative.");
        if (options.MaxLines < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxLines cannot be negative.");
        if (!Enum.IsDefined(options.WrapMode) || !Enum.IsDefined(options.Alignment) ||
            !Enum.IsDefined(options.Overflow))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.WrapMode != TextWrapMode.NoWrap && options.MaxWidth <= 0f)
            throw new ArgumentException("Automatic wrapping requires a positive MaxWidth.", nameof(options));
    }

    private static GlyphMetrics ValidateMetrics(GlyphMetrics metrics)
    {
        ValidateFinite(metrics.Advance, "glyph advance");
        ValidateFinite(metrics.BearingX, "glyph X bearing");
        ValidateFinite(metrics.BearingY, "glyph Y bearing");
        if (metrics.Advance < 0f) throw new InvalidOperationException("Glyph advance cannot be negative.");
        if (metrics.Width < 0 || metrics.Height < 0)
            throw new InvalidOperationException("Glyph dimensions cannot be negative.");
        return metrics;
    }

    private static float ValidateFinite(float value, string name)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException($"The rasterizer returned non-finite {name}.");
        return value;
    }
}

public readonly record struct TextLayouterDiagnostics(
    long LayoutCount,
    long BufferLayoutCount,
    long MissingGlyphCount);
