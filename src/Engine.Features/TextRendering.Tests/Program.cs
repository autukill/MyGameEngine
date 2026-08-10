namespace TextRendering.Tests;

using System.Numerics;
using System.Text;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextRendering.Domain;
using GameEngine.Features.TextRendering.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== TextRendering Feature Smoke Test ===\n");
        VerifyRegistrationAndLifetime();
        VerifyUnicodeLayoutAndFallback();
        VerifyAtlasAllocationAndCache();
        VerifyValidation();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All TextRendering smoke tests passed ==="
            : $"=== {_failures} TextRendering test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyRegistrationAndLifetime()
    {
        Console.WriteLine("1. Strict registration and resource ownership");
        var owned = LatinRasterizer();
        var borrowed = CjkRasterizer();
        var library = new FontLibrary();
        FontRef latin = library.Register("font.latin", Metadata("Latin"), owned);
        FontRef cjk = library.Register("font.cjk", Metadata("CJK"), borrowed, FontResourceOwnership.Borrowed);
        FontFamily family = library.CreateFamily(latin, cjk);

        Check(library.Count == 2 && family.Primary == latin && family.Fonts[1] == cjk,
            "Registered faces form an ordered fallback chain");
        Check(library.TryGetMetadata(cjk, out FontMetadata metadata) && metadata.FamilyName == "CJK",
            "Metadata is queryable by logical FontRef");
        CheckThrows<ArgumentException>(() => library.Register("font.latin", Metadata("Other"), new FakeRasterizer()),
            "Duplicate logical names are rejected");
        CheckThrows<ArgumentException>(() => library.CreateFamily(latin, latin),
            "Duplicate fallback entries are rejected");

        Check(library.Remove(cjk) && !borrowed.Disposed,
            "Borrowed rasterizer is not disposed when removed");
        library.Dispose();
        library.Dispose();
        Check(owned.DisposeCount == 1, "Owned rasterizer is disposed exactly once");
        CheckThrows<ObjectDisposedException>(() => _ = library.Count,
            "Disposed library rejects use");
    }

    private static void VerifyUnicodeLayoutAndFallback()
    {
        Console.WriteLine("2. Rune, grapheme and fallback-safe single-line layout");
        using var library = new FontLibrary();
        FontRef latin = library.Register("font.latin", Metadata("Latin"), LatinRasterizer());
        FontRef cjk = library.Register("font.cjk", Metadata("CJK"), CjkRasterizer());
        FontFamily family = library.CreateFamily(latin, cjk);
        var layouter = new SingleLineTextLayouter(library);
        const string text = "A\U0001F680e\u0301中?";
        SingleLineTextLayout layout = layouter.Layout(family, text, 20f);

        Check(layout.Glyphs.Count == 6, "UTF-16 surrogate pair produces one Rune glyph");
        Check(layout.Glyphs[1].Rune == new Rune(0x1F680), "Supplementary-plane Rune is preserved");
        Check(layout.Glyphs[3].ClusterStart == layout.Glyphs[2].ClusterStart &&
              layout.Glyphs[3].ClusterLength == 2,
            "Combining sequence shares one grapheme cluster boundary");
        Check(layout.Glyphs[4].Font == cjk, "CJK glyph resolves through ordered fallback");
        Check(layout.Glyphs[5].GlyphIndex == 0, "Unknown scalar resolves to primary missing glyph");
        Check(layout.ClusterStarts.Count == 5, "Cluster index exposes safe truncation boundaries");
        Check(Near(layout.Baseline, 16f) && Near(layout.Height, 22f) && layout.Width > 0,
            "Font metadata determines baseline, line height and measured width");

        var command = new TextDrawCommand(family, text, new Vector2(10, 20), 20f, Vector4.One);
        Check(command.Fonts == family && command.Text == text,
            "DrawText command remains logical and contains no GPU handle");
        CheckThrows<ArgumentException>(() => layouter.Layout(family, "a\nb", 12f),
            "Single-line API rejects line breaks explicitly");
    }

    private static void VerifyAtlasAllocationAndCache()
    {
        Console.WriteLine("3. Deterministic dynamic glyph atlas and cache");
        using var library = new FontLibrary();
        var rasterizer = LatinRasterizer();
        FontRef font = library.Register("font.latin", Metadata("Latin"), rasterizer);
        FontFamily family = library.CreateFamily(font);
        SingleLineTextLayout layout = new SingleLineTextLayouter(library).Layout(family, "AB A", 10f);
        var uploader = new FakeUploader();
        using (var atlas = new DynamicGlyphAtlas(
                   library,
                   uploader,
                   new GlyphAtlasOptions(PageWidth: 16, PageHeight: 16, Padding: 1, MaxPages: 4)))
        {
            PreparedTextLayout first = atlas.Prepare(layout);
            PreparedTextLayout second = atlas.Prepare(layout);

            Check(atlas.CachedGlyphCount == 3 && rasterizer.RasterizeCount == 3,
                "Repeated glyphs and repeated prepares hit one cache entry per key");
            Check(atlas.PageCount == 1 && uploader.Created.Count == 1,
                "Small glyph set shares one lazily-created page");
            Check(first.Glyphs[0].Atlas.SourceRect == new PixelRectI(1, 1, 4, 6) &&
                  first.Glyphs[1].Atlas.SourceRect == new PixelRectI(7, 1, 4, 6),
                "Shelf allocation is stable and padding is deterministic");
            Check(!first.Glyphs[2].Atlas.HasPixels && first.Glyphs[2].Atlas.Texture.IsEmpty,
                "Whitespace is cached without allocating atlas pixels");
            Check(first.Glyphs[0].Atlas == second.Glyphs[0].Atlas,
                "Prepared layouts reuse identical logical atlas entries");
        }

        Check(uploader.Deleted.Count == 1 && uploader.Deleted[0] == uploader.Created[0],
            "Atlas disposal deletes each owned logical page exactly once");
    }

    private static void VerifyValidation()
    {
        Console.WriteLine("4. Public boundary validation");
        var library = new FontLibrary();
        CheckThrows<ArgumentException>(() => library.Register("", Metadata("Bad"), new FakeRasterizer()),
            "Empty font names are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() =>
                library.Register("bad.metrics", new FontMetadata("Bad", 0, .8f, .2f), new FakeRasterizer()),
            "Invalid metadata is rejected");

        FontRef font = library.Register("font.valid", Metadata("Valid"), LatinRasterizer());
        var family = library.CreateFamily(font);
        CheckThrows<ArgumentOutOfRangeException>(() => new SingleLineTextLayouter(library).Layout(family, "A", 0),
            "Invalid pixel size is rejected");
        CheckThrows<ArgumentOutOfRangeException>(() =>
                new DynamicGlyphAtlas(library, new FakeUploader(), new GlyphAtlasOptions(0, 16, 1, 1)),
            "Invalid atlas options are rejected");
        using (var defaultAtlas = new DynamicGlyphAtlas(library, new FakeUploader()))
            Check(defaultAtlas.PageCount == 0, "Default atlas options are valid and pages remain lazy");
        library.Dispose();
    }

    private static FontMetadata Metadata(string family) => new(family, 1000, .8f, .2f, .1f);

    private static FakeRasterizer LatinRasterizer()
    {
        var rasterizer = new FakeRasterizer();
        rasterizer.Add('A', 1);
        rasterizer.Add('B', 2);
        rasterizer.Add('e', 3);
        rasterizer.Add('\u0301', 4, advance: 0);
        rasterizer.Add(' ', 5, width: 0, height: 0, advance: 3);
        rasterizer.Add(new Rune(0x1F680), 6);
        return rasterizer;
    }

    private static FakeRasterizer CjkRasterizer()
    {
        var rasterizer = new FakeRasterizer();
        rasterizer.Add('中', 10, width: 8, height: 8, advance: 10);
        return rasterizer;
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"   PASS: {name}");
            return;
        }

        _failures++;
        Console.WriteLine($"   FAIL: {name}");
    }

    private static void CheckThrows<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (TException)
        {
            Check(true, name);
        }
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < .0001f;

    private sealed class FakeRasterizer : IGlyphRasterizer, IDisposable
    {
        private readonly Dictionary<Rune, uint> _glyphs = [];
        private readonly Dictionary<uint, GlyphMetrics> _metrics = [];

        public uint MissingGlyphIndex => 0;
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public int RasterizeCount { get; private set; }

        public void Add(Rune rune, uint index, int width = 4, int height = 6, float advance = 5)
        {
            _glyphs.Add(rune, index);
            _metrics.Add(index, new GlyphMetrics(advance, 0, height, width, height));
        }

        public void Add(char character, uint index, int width = 4, int height = 6, float advance = 5) =>
            Add(new Rune(character), index, width, height, advance);

        public bool TryGetGlyphIndex(Rune rune, out uint glyphIndex) => _glyphs.TryGetValue(rune, out glyphIndex);

        public GlyphMetrics MeasureGlyph(uint glyphIndex, float pixelSize) =>
            _metrics.GetValueOrDefault(glyphIndex, new GlyphMetrics(5, 0, 6, 4, 6));

        public float MeasureKerning(uint leftGlyphIndex, uint rightGlyphIndex, float pixelSize) =>
            leftGlyphIndex == 1 && rightGlyphIndex == 2 ? -1 : 0;

        public GlyphBitmap RasterizeGlyph(uint glyphIndex, float pixelSize)
        {
            RasterizeCount++;
            GlyphMetrics metrics = MeasureGlyph(glyphIndex, pixelSize);
            return metrics.Width == 0
                ? GlyphBitmap.Empty
                : new GlyphBitmap(metrics.Width, metrics.Height,
                    Enumerable.Repeat((byte)255, metrics.Width * metrics.Height).ToArray());
        }

        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }

    private sealed class FakeUploader : IGlyphTextureUploader
    {
        public List<TextureRef> Created { get; } = [];
        public List<TextureRef> Deleted { get; } = [];
        public List<(TextureRef Texture, PixelRectI Rect, byte[] Pixels)> Uploads { get; } = [];

        public TextureRef CreateAlphaPage(string name, int width, int height)
        {
            var texture = new TextureRef(name);
            Created.Add(texture);
            return texture;
        }

        public void UploadAlpha(TextureRef texture, PixelRectI destination, ReadOnlySpan<byte> alphaPixels) =>
            Uploads.Add((texture, destination, alphaPixels.ToArray()));

        public void DeletePage(TextureRef texture) => Deleted.Add(texture);
    }
}
