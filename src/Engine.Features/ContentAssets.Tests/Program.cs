namespace ContentAssets.Tests;

using System.Text;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using SkiaSharp;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Content Assets Feature Smoke Test ===\n");
        VerifyManifestParsing();
        VerifyMultiImageIntegration();
        VerifySharedDependencyLifetime();
        VerifyGraphValidationAndRollback();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Content Assets smoke tests passed ==="
            : $"=== {_failures} Content Assets test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyManifestParsing()
    {
        Console.WriteLine("1. Strict versioned manifest parsing");
        const string json = """
            {
              "schemaVersion": 1,
              "id": "parser.assets",
              "dependencies": [{ "id": "shared", "manifest": "shared/assets.json" }],
              "textures": [
                { "name": "atlas", "path": "atlas.webp", "sampling": "pixelArt" },
                { "name": "frame.1", "path": "frame-1.webp" }
              ],
              "sprites": [
                {
                  "name": "single", "layout": "single", "texture": "atlas",
                  "source": { "x": 2, "y": 3, "width": 8, "height": 8 },
                  "origin": { "x": 4, "y": 7 }
                },
                {
                  "name": "grid", "layout": "grid", "texture": "atlas",
                  "frameSize": { "width": 8, "height": 8 }, "frameCount": 4,
                  "origin": { "x": 4, "y": 4 }, "framesPerSecond": 6
                },
                {
                  "name": "frames", "layout": "frames", "texture": "atlas",
                  "frames": [
                    {},
                    { "texture": "frame.1", "source": { "x": 1, "y": 1, "width": 8, "height": 8 } }
                  ],
                  "size": { "width": 10, "height": 12 },
                  "origin": { "x": 5, "y": 9 }
                }
              ]
            }
            """;

        var manifest = Parse(json);
        Check(manifest.SchemaVersion == 1 && manifest.Id == "parser.assets" &&
              manifest.Dependencies.Count == 1 && manifest.Textures.Count == 2,
            "Package identity, dependencies, and textures parse");
        Check(manifest.Sprites[0].SourceRect?.X == 2 &&
              manifest.Sprites[1].Layout == SpriteAssetLayout.Grid,
            "Single crop and Grid layout parse");
        Check(manifest.Sprites[2].Frames[0].TextureName is null &&
              manifest.Sprites[2].Frames[1].TextureName == "frame.1" &&
              manifest.Sprites[2].LogicalSize?.X == 10,
            "Frames default texture, override, crop, and logical size parse");

        CheckThrows<InvalidDataException>(() => Parse(json.Replace(
            "\"schemaVersion\": 1", "\"schemaVersion\": 2")),
            "Unknown schema version is rejected");
        CheckThrows<InvalidDataException>(() => Parse(json.Replace(
            "\"id\": \"parser.assets\"", "\"id\": \"parser.assets\", \"unknown\": true")),
            "Unknown fields are rejected");
        CheckThrows<InvalidDataException>(() => Parse("""
            { "schemaVersion": 1, "id": "bad", "textures": [], "sprites": [
              { "name": "bad", "layout": "frames", "frames": [{}],
                "origin": { "x": 0, "y": 0 } }
            ] }
            """),
            "Frames without a default or per-frame texture are rejected");
        CheckThrows<InvalidDataException>(() => Parse("""
            { "schemaVersion": 1, "id": "bad-origin", "textures": [
                { "name": "t", "path": "t.webp" }
              ], "sprites": [
                { "name": "s", "layout": "single", "texture": "t",
                  "origin": { "x": 0 } }
              ] }
            """),
            "Origin requires both named coordinates");
    }

    private static void VerifyMultiImageIntegration()
    {
        Console.WriteLine("2. Real WebP multi-image package integration");
        string root = Directory.CreateTempSubdirectory("mygame-content-multi-").FullName;
        try
        {
            WriteWebp(Path.Combine(root, "frame-0.webp"), 8, 8, SKColors.Orange);
            WriteWebp(Path.Combine(root, "frame-1.webp"), 12, 10, SKColors.Cyan);
            File.WriteAllText(Path.Combine(root, "assets.json"), """
                {
                  "schemaVersion": 1,
                  "id": "multi.assets",
                  "dependencies": [],
                  "textures": [
                    { "name": "multi.0", "path": "frame-0.webp", "sampling": "pixelArt" },
                    { "name": "multi.1", "path": "frame-1.webp", "sampling": "pixelArt" }
                  ],
                  "sprites": [{
                    "name": "multi.sprite", "layout": "frames",
                    "frames": [
                      { "texture": "multi.0", "source": { "x": 0, "y": 0, "width": 8, "height": 8 } },
                      { "texture": "multi.1", "source": { "x": 2, "y": 1, "width": 8, "height": 8 } }
                    ],
                    "origin": { "x": 4, "y": 6 }, "framesPerSecond": 8
                  }]
                }
                """);

            var backend = new FakeTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, root);
            using var package = manager.Load("assets.json");

            var sprite = package.GetSprite("multi.sprite");
            sprites.TryResolve(sprite, 0, out var first);
            sprites.TryResolve(sprite, 1, out var second);
            Check(first.TextureHandle != second.TextureHandle,
                "Different frames resolve to different GPU textures");
            Check(Near(second.UvBounds.X, 2f / 12f) && Near(second.UvBounds.Y, .1f) &&
                  Near(second.UvBounds.Z, 10f / 12f) && Near(second.UvBounds.W, .9f),
                "Per-frame pixel crop becomes texture-local UV bounds");
            Check(sprites.TryGetMetadata(sprite, out var metadata) &&
                  metadata.FrameCount == 2 && metadata.FramesPerSecond == 8,
                "Multi-image metadata is registered once");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifySharedDependencyLifetime()
    {
        Console.WriteLine("3. Dependency topology and reference-counted leases");
        string root = Directory.CreateTempSubdirectory("mygame-content-deps-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "shared"));
            Directory.CreateDirectory(Path.Combine(root, "a"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            WriteWebp(Path.Combine(root, "shared", "white.webp"), 4, 4, SKColors.White);
            File.WriteAllText(Path.Combine(root, "shared", "assets.json"), PackageJson(
                "shared.assets",
                "\"textures\":[{\"name\":\"shared.white\",\"path\":\"white.webp\"}],\"sprites\":[]"));
            File.WriteAllText(Path.Combine(root, "a", "assets.json"), DependentSpriteJson(
                "a.assets", "a.sprite", "shared/assets.json"));
            File.WriteAllText(Path.Combine(root, "b", "assets.json"), DependentSpriteJson(
                "b.assets", "b.sprite", "shared/assets.json"));

            var backend = new FakeTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, root);

            var a1 = manager.Load("a/assets.json");
            var a2 = manager.Load("a/assets.json");
            var b = manager.Load("b/assets.json");
            Check(manager.LoadedPackageCount == 3 && textures.Count == 1 && sprites.Count == 2,
                "Shared dependency is assembled once across roots and duplicate leases");
            Check(a1.GetTexture("shared.white").Name == "shared.white",
                "Transitive dependency assets are visible to a root lease");

            a1.Dispose();
            a2.Dispose();
            Check(textures.Count == 1 && sprites.Count == 1,
                "Shared dependency survives while another root holds it");
            b.Dispose();
            b.Dispose();
            Check(manager.LoadedPackageCount == 0 && textures.Count == 0 && sprites.Count == 0 &&
                  backend.Deleted.Count == 1,
                "Final idempotent lease disposal unloads Sprite then shared Texture");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void VerifyGraphValidationAndRollback()
    {
        Console.WriteLine("4. Graph validation, safe paths, and atomic rollback");
        string root = Directory.CreateTempSubdirectory("mygame-content-fail-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "cycle"));
            File.WriteAllText(Path.Combine(root, "cycle", "a.json"), """
                { "schemaVersion":1, "id":"cycle.a",
                  "dependencies":[{"id":"cycle.b","manifest":"cycle/b.json"}],
                  "textures":[], "sprites":[{"name":"a","layout":"single","texture":"never",
                    "origin":{"x":0,"y":0}}] }
                """);
            File.WriteAllText(Path.Combine(root, "cycle", "b.json"), """
                { "schemaVersion":1, "id":"cycle.b",
                  "dependencies":[{"id":"cycle.a","manifest":"cycle/a.json"}],
                  "textures":[], "sprites":[{"name":"b","layout":"single","texture":"never",
                    "origin":{"x":0,"y":0}}] }
                """);

            File.WriteAllBytes(Path.Combine(root, "good.webp"), CreateWebpBytes(2, 2, SKColors.Red));
            File.WriteAllBytes(Path.Combine(root, "bad.webp"), [1, 2, 3, 4]);
            File.WriteAllText(Path.Combine(root, "rollback.json"), """
                { "schemaVersion":1, "id":"rollback.assets", "dependencies":[],
                  "textures":[
                    {"name":"rollback.good","path":"good.webp"},
                    {"name":"rollback.bad","path":"bad.webp"}
                  ], "sprites":[] }
                """);
            File.WriteAllText(Path.Combine(root, "escape.json"), """
                { "schemaVersion":1, "id":"escape.assets", "dependencies":[],
                  "textures":[{"name":"escape","path":"../outside.webp"}], "sprites":[] }
                """);
            File.WriteAllText(Path.Combine(root, "outside-closure.json"), """
                { "schemaVersion":1, "id":"outside.assets", "dependencies":[],
                  "textures":[], "sprites":[{
                    "name":"outside.sprite", "layout":"single", "texture":"preexisting",
                    "origin":{"x":0,"y":0}
                  }] }
                """);

            var backend = new FakeTextureBackend();
            using var textures = new TextureLibrary(backend);
            textures.RegisterRgba("preexisting", 1, 1, new byte[4]);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, root);

            CheckThrows<InvalidDataException>(() => manager.Load("cycle/a.json"),
                "Dependency cycles are rejected before GPU mutation");
            CheckThrows<InvalidDataException>(() => manager.Load("escape.json"),
                "Texture paths cannot escape their package directory");
            CheckThrows<InvalidDataException>(() => manager.Load("outside-closure.json"),
                "A globally registered Texture outside the dependency closure is rejected");
            CheckThrows<InvalidDataException>(() => manager.Load("rollback.json"),
                "A later decode failure is surfaced");
            Check(manager.LoadedPackageCount == 0 && textures.Count == 1 && sprites.Count == 0 &&
                  textures.TryGetMetadata(new TextureRef("preexisting"), out _),
                "Failed load rolls back only newly owned resources and reference counts");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AssetPackageManifest Parse(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AssetPackageManifestParser.Parse(stream);
    }

    private static string PackageJson(string id, string body) =>
        $"{{\"schemaVersion\":1,\"id\":\"{id}\",\"dependencies\":[],{body}}}";

    private static string DependentSpriteJson(string id, string sprite, string dependencyManifest) =>
        $$"""
          { "schemaVersion":1, "id":"{{id}}",
            "dependencies":[{"id":"shared.assets","manifest":"{{dependencyManifest}}"}],
            "textures":[], "sprites":[{
              "name":"{{sprite}}", "layout":"single", "texture":"shared.white",
              "origin":{"x":2,"y":2}
            }] }
          """;

    private static void WriteWebp(string path, int width, int height, SKColor color) =>
        File.WriteAllBytes(path, CreateWebpBytes(width, height, color));

    private static byte[] CreateWebpBytes(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 100)
            ?? throw new InvalidOperationException("Could not encode WebP fixture.");
        return data.ToArray();
    }

    private static bool Near(float left, float right) => MathF.Abs(left - right) < .0001f;

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

    private sealed class FakeTextureBackend : ITextureBackend
    {
        private uint _nextHandle = 1;
        public List<uint> Deleted { get; } = [];

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler) => _nextHandle++;

        public void DeleteTexture(uint handle) => Deleted.Add(handle);
    }
}
