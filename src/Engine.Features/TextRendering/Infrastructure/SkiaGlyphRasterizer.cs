namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Runtime.InteropServices;
using System.Text;
using GameEngine.Features.TextRendering.Domain;
using SkiaSharp;

/// <summary>Real TrueType/OpenType glyph metrics and Alpha8 rasterization backed by SkiaSharp.</summary>
public sealed class SkiaGlyphRasterizer : IGlyphRasterizer, IDisposable
{
    private readonly SKTypeface _typeface;
    private readonly Dictionary<int, SKFont> _fonts = [];
    private bool _disposed;

    private SkiaGlyphRasterizer(SKTypeface typeface)
    {
        _typeface = typeface ?? throw new ArgumentNullException(nameof(typeface));
        int unitsPerEm = checked((int)_typeface.UnitsPerEm);
        using var font = CreateFont(unitsPerEm);
        font.GetFontMetrics(out SKFontMetrics metrics);
        Metadata = new FontMetadata(
            string.IsNullOrWhiteSpace(typeface.FamilyName) ? "Unknown" : typeface.FamilyName,
            unitsPerEm,
            MathF.Max(0.0001f, -metrics.Ascent / unitsPerEm),
            MathF.Max(0f, metrics.Descent / unitsPerEm),
            MathF.Max(0f, metrics.Leading / unitsPerEm));
    }

    public FontMetadata Metadata { get; }
    public uint MissingGlyphIndex => 0;

    public static SkiaGlyphRasterizer Load(string path, int faceIndex = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (faceIndex < 0) throw new ArgumentOutOfRangeException(nameof(faceIndex));
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Font file was not found.", fullPath);
        SKTypeface typeface = SKTypeface.FromFile(fullPath, faceIndex)
            ?? throw new InvalidDataException($"Skia could not parse font '{fullPath}'.");
        return new SkiaGlyphRasterizer(typeface);
    }

    public static SkiaGlyphRasterizer Load(Stream stream, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Font stream must be readable.", nameof(stream));
        if (faceIndex < 0) throw new ArgumentOutOfRangeException(nameof(faceIndex));
        SKTypeface typeface = SKTypeface.FromStream(stream, faceIndex)
            ?? throw new InvalidDataException("Skia could not parse the font stream.");
        return new SkiaGlyphRasterizer(typeface);
    }

    public static SkiaGlyphRasterizer FromFamilyName(string familyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        SKTypeface typeface = SKTypeface.FromFamilyName(familyName)
            ?? throw new InvalidOperationException($"System font family '{familyName}' is unavailable.");
        return new SkiaGlyphRasterizer(typeface);
    }

    public bool TryGetGlyphIndex(Rune rune, out uint glyphIndex)
    {
        ThrowIfDisposed();
        glyphIndex = GetFont(16f).GetGlyph(rune.Value);
        return glyphIndex != MissingGlyphIndex;
    }

    public GlyphMetrics MeasureGlyph(uint glyphIndex, float pixelSize)
    {
        ThrowIfDisposed();
        ValidateGlyph(glyphIndex);
        SKFont font = GetFont(pixelSize);
        Span<ushort> glyphs = stackalloc ushort[1] { (ushort)glyphIndex };
        Span<float> widths = stackalloc float[1];
        Span<SKRect> bounds = stackalloc SKRect[1];
        font.GetGlyphWidths(glyphs, widths, bounds, paint: null);
        GetPixelBounds(bounds[0], out int left, out int top, out int width, out int height);
        return new GlyphMetrics(widths[0], left, -top, width, height);
    }

    public float MeasureKerning(uint leftGlyphIndex, uint rightGlyphIndex, float pixelSize)
    {
        ThrowIfDisposed();
        ValidateGlyph(leftGlyphIndex);
        ValidateGlyph(rightGlyphIndex);
        _ = GetFont(pixelSize);
        // Skia's low-level SKFont API exposes unshaped glyph advances. HarfBuzz shaping is a later slice.
        return 0f;
    }

    public GlyphBitmap RasterizeGlyph(uint glyphIndex, float pixelSize)
    {
        ThrowIfDisposed();
        GlyphMetrics metrics = MeasureGlyph(glyphIndex, pixelSize);
        if (metrics.Width == 0 || metrics.Height == 0) return GlyphBitmap.Empty;

        SKFont font = GetFont(pixelSize);
        using var bitmap = new SKBitmap(new SKImageInfo(
            metrics.Width,
            metrics.Height,
            SKColorType.Alpha8,
            SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var builder = new SKTextBlobBuilder();
        Span<ushort> glyphs = stackalloc ushort[1] { (ushort)glyphIndex };
        builder.AddRun(glyphs, font, new SKPoint(-metrics.BearingX, metrics.BearingY));
        using SKTextBlob? blob = builder.Build();
        if (blob is null) throw new InvalidOperationException("Skia failed to build a glyph run.");
        canvas.Clear(SKColors.Transparent);
        canvas.DrawText(blob, 0f, 0f, paint);
        canvas.Flush();

        var alpha = new byte[checked(metrics.Width * metrics.Height)];
        IntPtr pixels = bitmap.GetPixels();
        for (int row = 0; row < metrics.Height; row++)
        {
            Marshal.Copy(
                IntPtr.Add(pixels, checked(row * bitmap.RowBytes)),
                alpha,
                row * metrics.Width,
                metrics.Width);
        }
        return new GlyphBitmap(metrics.Width, metrics.Height, alpha);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (SKFont font in _fonts.Values) font.Dispose();
        _fonts.Clear();
        _typeface.Dispose();
        _disposed = true;
    }

    private SKFont GetFont(float pixelSize)
    {
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        int key = BitConverter.SingleToInt32Bits(pixelSize);
        if (_fonts.TryGetValue(key, out SKFont? font)) return font;
        font = CreateFont(pixelSize);
        _fonts.Add(key, font);
        return font;
    }

    private SKFont CreateFont(float size) => new(_typeface, size)
    {
        Edging = SKFontEdging.Antialias,
        Subpixel = true
    };

    private static void GetPixelBounds(
        SKRect bounds,
        out int left,
        out int top,
        out int width,
        out int height)
    {
        left = (int)MathF.Floor(bounds.Left);
        top = (int)MathF.Floor(bounds.Top);
        int right = (int)MathF.Ceiling(bounds.Right);
        int bottom = (int)MathF.Ceiling(bounds.Bottom);
        width = Math.Max(0, right - left);
        height = Math.Max(0, bottom - top);
    }

    private static void ValidateGlyph(uint glyphIndex)
    {
        if (glyphIndex > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(glyphIndex), "Skia glyph indices are 16-bit.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
