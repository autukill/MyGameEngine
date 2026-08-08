namespace AssetCompiler.Tests;

using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Tools.AssetCompiler;
using SkiaSharp;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Asset Compiler Smoke Test ===\n");
        VerifyCompileAndRuntimeLoad();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Asset Compiler smoke tests passed ==="
            : $"=== {_failures} Asset Compiler test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyCompileAndRuntimeLoad()
    {
        Console.WriteLine("1. Deterministic offline build and runtime compatibility");
        string workspace = Directory.CreateTempSubdirectory("mygame-compiler-").FullName;
        string source = Path.Combine(workspace, "source");
        string firstOutput = Path.Combine(workspace, "compiled-a");
        string secondOutput = Path.Combine(workspace, "compiled-b");
        Directory.CreateDirectory(source);
        try
        {
            WriteSheet(Path.Combine(source, "sheet.png"));
            WriteSolid(Path.Combine(source, "large.png"), 7, 7, SKColors.Blue);
            Directory.CreateDirectory(Path.Combine(source, "shared"));
            WriteSolid(Path.Combine(source, "shared", "white.png"), 1, 1, SKColors.White);
            File.WriteAllText(Path.Combine(source, "shared", "assets.json"), SharedManifest);
            File.WriteAllText(Path.Combine(source, "assets.json"), SourceManifest);

            var compiler = new ContentAssetCompiler();
            var first = compiler.Compile(source, "assets.json", firstOutput);
            var second = compiler.Compile(source, "assets.json", secondOutput);

            Check(first.AtlasPageCount == 2 && first.PackedFrameCount == 2 &&
                  first.PassthroughFrameCount == 1,
                "Two small frames become two constrained pages and the large frame bypasses");
            Check(DirectoriesEqual(firstOutput, secondOutput),
                "Repeated builds produce byte-identical artifacts");

            string compiledJson = File.ReadAllText(Path.Combine(firstOutput, "assets.json"));
            Check(!compiledJson.Contains("sheet.png", StringComparison.Ordinal) &&
                  compiledJson.Contains("large.png", StringComparison.Ordinal) &&
                  compiledJson.Contains("atlas/pixel-art-0.png", StringComparison.Ordinal),
                "Fully packed source Texture is removed while passthrough source is retained");
            Check(File.Exists(Path.Combine(firstOutput, "shared", "assets.json")) &&
                  File.Exists(Path.Combine(firstOutput, "shared", "white.png")),
                "Dependency packages are copied into the compiled packages root");

            var backend = new FakeTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, firstOutput);
            using var package = manager.Load("assets.json");

            var grid = package.GetSprite("compiler.grid");
            sprites.TryResolve(grid, 0, out var frame0);
            sprites.TryResolve(grid, 1, out var frame1);
            var large = package.GetSprite("compiler.large");
            sprites.TryResolve(large, 0, out var largeFrame);
            Check(frame0.TextureHandle != frame1.TextureHandle,
                "Compiled animation can cross Atlas pages");
            Check(largeFrame.TextureHandle != frame0.TextureHandle &&
                  largeFrame.TextureHandle != frame1.TextureHandle,
                "Oversized frame remains on its independent Texture");
            Check(textures.Count == 4 && sprites.Count == 2,
                "Existing ContentPackageManager loads the compiled standard package");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static bool DirectoriesEqual(string left, string right)
    {
        string[] leftFiles = Directory.GetFiles(left, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(left, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] rightFiles = Directory.GetFiles(right, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(right, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!leftFiles.SequenceEqual(rightFiles, StringComparer.Ordinal)) return false;
        return leftFiles.All(relative =>
            File.ReadAllBytes(Path.Combine(left, relative)).SequenceEqual(
                File.ReadAllBytes(Path.Combine(right, relative))));
    }

    private static void WriteSheet(string path)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(4, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 4; x++)
            bitmap.SetPixel(x, y, x < 2 ? SKColors.Red : SKColors.Lime);
        WritePng(path, bitmap);
    }

    private static void WriteSolid(string path, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        WritePng(path, bitmap);
    }

    private static void WritePng(string path, SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Could not encode compiler test fixture.");
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else { _failures++; Console.WriteLine($"  [FAIL] {name}"); }
    }

    private sealed class FakeTextureBackend : ITextureBackend
    {
        private uint _next = 1;
        public uint CreateTexture(
            int width, int height, ReadOnlySpan<byte> rgbaPixels, TextureSampler sampler) => _next++;
        public void DeleteTexture(uint handle) { }
    }

    private const string SourceManifest = """
        {
          "schemaVersion": 1,
          "id": "compiler.assets",
          "dependencies": [
            { "id": "compiler.shared", "manifest": "shared/assets.json" }
          ],
          "atlas": {
            "maxPageSize": { "width": 6, "height": 6 },
            "padding": 0,
            "extrude": 1,
            "textures": ["compiler.sheet", "compiler.large"]
          },
          "textures": [
            { "name": "compiler.sheet", "path": "sheet.png", "sampling": "pixelArt" },
            { "name": "compiler.large", "path": "large.png", "sampling": "pixelArt" }
          ],
          "sprites": [
            {
              "name": "compiler.grid",
              "layout": "grid",
              "texture": "compiler.sheet",
              "frameSize": { "width": 2, "height": 2 },
              "frameCount": 2,
              "origin": { "x": 1, "y": 1 },
              "framesPerSecond": 4
            },
            {
              "name": "compiler.large",
              "layout": "single",
              "texture": "compiler.large",
              "origin": { "x": 3, "y": 3 }
            }
          ]
        }
        """;

    private const string SharedManifest = """
        {
          "schemaVersion": 1,
          "id": "compiler.shared",
          "dependencies": [],
          "textures": [
            { "name": "compiler.white", "path": "white.png", "sampling": "smooth" }
          ],
          "sprites": []
        }
        """;
}
