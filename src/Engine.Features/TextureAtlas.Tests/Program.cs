namespace TextureAtlas.Tests;

using GameEngine.Features.TextureAtlas.Domain;
using GameEngine.Features.TextureAtlas.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Texture Atlas Feature Smoke Test ===\n");
        VerifyPackingAndPixels();
        VerifyMultiPageAndPassthrough();
        VerifyDeterminismAndValidation();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Texture Atlas smoke tests passed ==="
            : $"=== {_failures} Texture Atlas test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyPackingAndPixels()
    {
        Console.WriteLine("1. Placement, padding, and edge extrusion");
        var builder = new TextureAtlasBuilder();
        var red = Solid("red", 2, 2, 255, 0, 0, 255);
        var green = Solid("green", 1, 2, 0, 255, 0, 255);
        var result = builder.Build([red, green], new AtlasBuildOptions(16, 16, Padding: 1, Extrude: 1));

        Check(result.Pages.Count == 1 && result.Placements.Count == 2,
            "Two fitting frames share one page");
        var redPlacement = result.Placements["red"];
        var greenPlacement = result.Placements["green"];
        Check(!Overlaps(redPlacement.SourceRect, greenPlacement.SourceRect),
            "Content rectangles do not overlap");

        AtlasPage page = result.Pages[0];
        Check(Pixel(page, redPlacement.SourceRect.X, redPlacement.SourceRect.Y) == (255, 0, 0, 255),
            "Frame pixels are copied exactly");
        Check(Pixel(page, redPlacement.SourceRect.X - 1, redPlacement.SourceRect.Y) == (255, 0, 0, 255),
            "Extrude duplicates the nearest edge pixel");
        Check(Pixel(page, redPlacement.SourceRect.X - 2, redPlacement.SourceRect.Y) == (0, 0, 0, 0),
            "Padding outside extrusion remains transparent");
    }

    private static void VerifyMultiPageAndPassthrough()
    {
        Console.WriteLine("2. Multiple pages and oversized-frame bypass");
        var builder = new TextureAtlasBuilder();
        var result = builder.Build(
            [
                Solid("a", 4, 4, 1, 2, 3, 255),
                Solid("b", 4, 4, 4, 5, 6, 255),
                Solid("large", 9, 9, 7, 8, 9, 255)
            ],
            new AtlasBuildOptions(6, 6, Padding: 0, Extrude: 0));

        Check(result.Pages.Count == 2,
            "Frames that cannot share a page spill deterministically to another page");
        Check(result.Placements["a"].PageIndex != result.Placements["b"].PageIndex,
            "Multi-page placements carry their page index");
        Check(result.PassthroughKeys.SetEquals(["large"]),
            "A frame larger than page limits is returned as passthrough");
    }

    private static void VerifyDeterminismAndValidation()
    {
        Console.WriteLine("3. Determinism and assembly-time validation");
        var frames = new[]
        {
            Solid("z", 3, 2, 10, 20, 30, 255),
            Solid("a", 2, 3, 40, 50, 60, 255),
            Solid("m", 2, 2, 70, 80, 90, 255)
        };
        var builder = new TextureAtlasBuilder();
        var first = builder.Build(frames, new AtlasBuildOptions(16, 16));
        var second = builder.Build(frames.Reverse(), new AtlasBuildOptions(16, 16));

        Check(first.Placements.OrderBy(item => item.Key).SequenceEqual(
                second.Placements.OrderBy(item => item.Key)) &&
              first.Pages.SelectMany(page => page.RgbaPixels).SequenceEqual(
                second.Pages.SelectMany(page => page.RgbaPixels)),
            "Input order does not affect placement or pixels");
        CheckThrows<ArgumentException>(() => builder.Build(
                [frames[0], frames[0]], AtlasBuildOptions.Default),
            "Duplicate frame keys are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => builder.Build(
                frames, new AtlasBuildOptions(0, 16)),
            "Invalid page dimensions are rejected");
    }

    private static AtlasSourceFrame Solid(
        string key, int width, int height, byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        return new AtlasSourceFrame(key, width, height, pixels);
    }

    private static (byte R, byte G, byte B, byte A) Pixel(AtlasPage page, int x, int y)
    {
        int offset = (y * page.Width + x) * 4;
        return (
            page.RgbaPixels[offset],
            page.RgbaPixels[offset + 1],
            page.RgbaPixels[offset + 2],
            page.RgbaPixels[offset + 3]);
    }

    private static bool Overlaps(
        GameEngine.Core.Domain.Graphics.PixelRectI a,
        GameEngine.Core.Domain.Graphics.PixelRectI b) =>
        a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else { _failures++; Console.WriteLine($"  [FAIL] {name}"); }
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
    }
}
