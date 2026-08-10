namespace AssetCompiler.Tests;

using GameEngine.Features.Animation;
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
        VerifyShaderReferenceGeneration();

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
                  compiledJson.Contains("atlas/pixel-art-0.png", StringComparison.Ordinal) &&
                  compiledJson.Contains("compiler.grid.run", StringComparison.Ordinal),
                "Atlas remap preserves declarative Animation assets");
            Check(File.Exists(Path.Combine(firstOutput, "shared", "assets.json")) &&
                  File.Exists(Path.Combine(firstOutput, "shared", "white.png")),
                "Dependency packages are copied into the compiled packages root");

            VerifyStronglyTypedReferences(firstOutput, workspace);

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
            AnimationClip animation = manager.Animations.Get(
                package.GetAnimation("compiler.grid.run"));
            Check(animation.Sprite == grid && animation.SubImages.SequenceEqual([0, 1]) &&
                  animation.Markers[0].Event == new AnimationEventRef("compiler.step"),
                "Compiled package loads Animation against the remapped Sprite");

            VerifyIncrementalPipeline(source, workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static void VerifyStronglyTypedReferences(string compiledRoot, string workspace)
    {
        Console.WriteLine("2. Strongly typed runtime references");
        string output = Path.Combine(workspace, "generated", "GameEngine.Content.g.cs");
        var generator = new ContentReferenceCodeGenerator();
        var request = new ContentReferenceGenerationRequest(
            compiledRoot,
            "assets.json",
            output,
            "Compiler.Sample.Content");

        ContentReferenceGenerationResult first = generator.Generate(request);
        DateTime firstWrite = File.GetLastWriteTimeUtc(output);
        ContentReferenceGenerationResult cached = generator.Generate(request);
        string source = File.ReadAllText(output);
        Check(first.Changed && !cached.Changed && File.GetLastWriteTimeUtc(output) == firstWrite,
            "Unchanged generated references preserve the file timestamp");
        Check(first.PackageCount == 2 && first.TextureCount == 2 && first.SpriteCount == 2 &&
              first.AnimationCount == 1 && first.AnimationEventCount == 1 &&
              source.Contains("ContentPackageRef Root = new(\"compiler.assets\", \"assets.json\")", StringComparison.Ordinal) &&
              source.Contains("ContentPackageRef CompilerShared", StringComparison.Ordinal),
            "Root and dependency package references are generated from the compiled graph");
        Check(source.Contains("TextureRef CompilerLarge", StringComparison.Ordinal) &&
              source.Contains("TextureRef CompilerWhite", StringComparison.Ordinal) &&
              !source.Contains("CompilerSheet", StringComparison.Ordinal) &&
              !source.Contains("__atlas.", StringComparison.Ordinal),
            "Only public runtime Textures are exposed across the Atlas boundary");
        Check(source.Contains("SpriteRef CompilerGrid", StringComparison.Ordinal) &&
              source.Contains("SpriteRef CompilerLarge", StringComparison.Ordinal),
            "Compiled Sprite names remain stable typed references");
        Check(source.Contains("AnimationClipRef CompilerGridRun", StringComparison.Ordinal) &&
              source.Contains("AnimationEventRef CompilerStep", StringComparison.Ordinal),
            "Animation clips and marker events become strongly typed references");
        CheckThrows<InvalidDataException>(() => generator.Generate(request with
            {
                OutputFile = Path.Combine(compiledRoot, "GameEngine.Content.g.cs")
            }),
            "Generated source cannot overwrite files inside the compiled package root");

        string collisionRoot = Path.Combine(workspace, "collision");
        Directory.CreateDirectory(collisionRoot);
        File.WriteAllText(Path.Combine(collisionRoot, "assets.json"), CollisionManifest);
        CheckThrows<InvalidDataException>(() => generator.Generate(request with
            {
                CompiledPackagesRoot = collisionRoot,
                OutputFile = Path.Combine(collisionRoot, "collision.g.cs")
            }),
            "Ambiguous normalized C# identifiers fail with a build-time diagnostic");
    }

    private static void VerifyIncrementalPipeline(string source, string workspace)
    {
        Console.WriteLine("3. Incremental graph build, check mode, and failure safety");
        string output = Path.Combine(workspace, "incremental");
        var pipeline = new ContentBuildPipeline();
        var request = new ContentBuildRequest(source, "assets.json", output);

        ContentBuildResult first = pipeline.Build(request);
        byte[] firstMetadata = File.ReadAllBytes(
            Path.Combine(output, ContentBuildPipeline.MetadataFileName));
        ContentBuildResult cached = pipeline.Build(request);
        ContentBuildResult current = pipeline.Build(request with { Mode = ContentBuildMode.Check });
        Check(first.Status == ContentBuildStatus.Built &&
              first.BuiltPackageCount == 2 && first.ReusedPackageCount == 0 &&
              cached.Status == ContentBuildStatus.UpToDate &&
              cached.BuiltPackageCount == 0 && cached.ReusedPackageCount == 2 &&
              current.Status == ContentBuildStatus.UpToDate &&
              first.InputFingerprint == cached.InputFingerprint,
            "Unchanged dependency graph is skipped and check mode reports current");
        Check(firstMetadata.SequenceEqual(File.ReadAllBytes(
                Path.Combine(output, ContentBuildPipeline.MetadataFileName))),
            "Cache hit performs no metadata rewrite");

        WriteSolid(Path.Combine(source, "large.png"), 7, 7, SKColors.Purple);
        ContentBuildResult stale = pipeline.Build(request with { Mode = ContentBuildMode.Check });
        Check(stale.Status == ContentBuildStatus.Stale &&
              firstMetadata.SequenceEqual(File.ReadAllBytes(
                  Path.Combine(output, ContentBuildPipeline.MetadataFileName))),
            "Changed source is detected without writing in check mode");

        ContentBuildResult rebuilt = pipeline.Build(request);
        Check(rebuilt.Status == ContentBuildStatus.Built &&
              rebuilt.InputFingerprint != first.InputFingerprint &&
              rebuilt.BuiltPackageCount == 1 && rebuilt.ReusedPackageCount == 1,
            "Changed root source rebuilds only that package and reuses its dependency");

        WriteSolid(Path.Combine(source, "shared", "white.png"), 1, 1, SKColors.Gray);
        ContentBuildResult dependencyRebuilt = pipeline.Build(request);
        byte[] validMetadata = File.ReadAllBytes(
            Path.Combine(output, ContentBuildPipeline.MetadataFileName));
        Check(dependencyRebuilt.BuiltPackageCount == 2 &&
              dependencyRebuilt.ReusedPackageCount == 0,
            "Changed dependency invalidates itself and its upstream root package");

        File.WriteAllBytes(Path.Combine(source, "sheet.png"), [1, 2, 3, 4]);
        CheckThrows<InvalidDataException>(() => pipeline.Build(
                request with { Mode = ContentBuildMode.Rebuild }),
            "Decode failure is surfaced during forced rebuild");
        Check(validMetadata.SequenceEqual(File.ReadAllBytes(
                Path.Combine(output, ContentBuildPipeline.MetadataFileName))) &&
              File.Exists(Path.Combine(output, "assets.json")),
            "Failed rebuild preserves the previous valid output atomically");
        WriteSheet(Path.Combine(source, "sheet.png"));

        string foreign = Path.Combine(workspace, "foreign-output");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "user.txt"), "keep");
        CheckThrows<IOException>(() => pipeline.Build(new ContentBuildRequest(
                source, "assets.json", foreign, ContentBuildMode.Rebuild)),
            "Non-empty output without compiler ownership is never overwritten");
        Check(File.ReadAllText(Path.Combine(foreign, "user.txt")) == "keep",
            "Foreign output remains untouched");
    }

    private static void VerifyShaderReferenceGeneration()
    {
        Console.WriteLine("4. Strongly typed Shader, Material, and parameter references");
        string workspace = Directory.CreateTempSubdirectory("mygame-shader-refs-").FullName;
        string projectRoot = Path.Combine(workspace, "game");
        string shadersRoot = Path.Combine(projectRoot, "Shaders");
        string output = Path.Combine(projectRoot, "obj", "GameEngine.Shaders.g.cs");
        Directory.CreateDirectory(shadersRoot);
        try
        {
            File.WriteAllText(Path.Combine(shadersRoot, "sprite.vert.glsl"), "void main() {}");
            File.WriteAllText(Path.Combine(shadersRoot, "orbit.frag.glsl"), "void main() {}");
            string manifest = Path.Combine(shadersRoot, "shaders.json");
            File.WriteAllText(manifest, ShaderManifest);

            var generator = new ShaderReferenceCodeGenerator();
            var request = new ShaderReferenceGenerationRequest(
                projectRoot,
                manifest,
                output,
                "Compiler.Sample.Content");
            ShaderReferenceGenerationResult first = generator.Generate(request);
            DateTime firstWrite = File.GetLastWriteTimeUtc(output);
            ShaderReferenceGenerationResult cached = generator.Generate(request);
            string source = File.ReadAllText(output);

            Check(first.Changed && !cached.Changed &&
                  File.GetLastWriteTimeUtc(output) == firstWrite &&
                  first.ShaderCount == 1 && first.MaterialCount == 1 &&
                  first.ParameterCount == 4,
                "Unchanged Shader references are deterministic and preserve timestamps");
            Check(source.Contains("const string ManifestPath = \"Shaders/shaders.json\"", StringComparison.Ordinal) &&
                  source.Contains("ShaderRef RunnerOrbit", StringComparison.Ordinal) &&
                  source.Contains("MaterialRef RunnerOrbitMaterial", StringComparison.Ordinal),
                "Manifest, Shader, and Material logical references are generated");
            Check(source.Contains("MaterialParameterRef<float> Gain", StringComparison.Ordinal) &&
                  source.Contains("MaterialParameterRef<int> Mode", StringComparison.Ordinal) &&
                  source.Contains("MaterialParameterRef<global::System.Numerics.Vector2> Direction", StringComparison.Ordinal) &&
                  source.Contains("MaterialParameterRef<global::System.Numerics.Vector4> Tint", StringComparison.Ordinal),
                "Uniform schema produces strongly typed parameter keys without the conventional u prefix");

            File.WriteAllText(Path.Combine(shadersRoot, "collision.json"), ShaderCollisionManifest);
            CheckThrows<InvalidDataException>(() => generator.Generate(request with
                {
                    ManifestPath = Path.Combine(shadersRoot, "collision.json"),
                    OutputFile = Path.Combine(projectRoot, "obj", "collision.g.cs")
                }),
                "Ambiguous Shader identifiers fail during generation");

            File.WriteAllText(
                Path.Combine(shadersRoot, "parameter-collision.json"),
                ShaderParameterCollisionManifest);
            CheckThrows<InvalidDataException>(() => generator.Generate(request with
                {
                    ManifestPath = Path.Combine(shadersRoot, "parameter-collision.json"),
                    OutputFile = Path.Combine(projectRoot, "obj", "parameter-collision.g.cs")
                }),
                "Ambiguous normalized parameter identifiers fail during generation");

            string outside = Path.Combine(workspace, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "sprite.vert.glsl"), "void main() {}");
            File.WriteAllText(Path.Combine(outside, "orbit.frag.glsl"), "void main() {}");
            File.WriteAllText(Path.Combine(outside, "shaders.json"), ShaderManifest);
            CheckThrows<InvalidDataException>(() => generator.Generate(request with
                {
                    ManifestPath = Path.Combine(outside, "shaders.json")
                }),
                "Generated runtime manifest paths cannot escape the project root");
            CheckThrows<InvalidDataException>(() => generator.Generate(request with
                {
                    OutputFile = Path.Combine(workspace, "escaped.g.cs")
                }),
                "Generated Shader source cannot escape the project root");
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

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
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
          ],
          "animations": [
            {
              "name": "compiler.grid.run",
              "sprite": "compiler.grid",
              "frames": [0, 1],
              "framesPerSecond": 8,
              "loop": "loop",
              "markers": [{ "frame": 1, "event": "compiler.step" }]
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

    private const string CollisionManifest = """
        {
          "schemaVersion": 1,
          "id": "collision.assets",
          "dependencies": [],
          "textures": [
            { "name": "foo-bar", "path": "first.png" },
            { "name": "foo.bar", "path": "second.png" }
          ],
          "sprites": []
        }
        """;

    private const string ShaderManifest = """
        {
          "schemaVersion": 1,
          "shaders": [
            {
              "name": "runner.orbit",
              "vertex": "sprite.vert.glsl",
              "fragment": "orbit.frag.glsl"
            }
          ],
          "materials": [
            {
              "name": "runner.orbit.material",
              "shader": "runner.orbit",
              "uniforms": [
                { "name": "uGain", "type": "float", "default": 1.0 },
                { "name": "uMode", "type": "int", "default": 0 },
                { "name": "uDirection", "type": "vector2", "default": { "x": 1, "y": 0 } },
                { "name": "uTint", "type": "vector4", "default": { "x": 1, "y": 1, "z": 1, "w": 1 } }
              ]
            }
          ]
        }
        """;

    private const string ShaderCollisionManifest = """
        {
          "schemaVersion": 1,
          "shaders": [
            { "name": "foo-bar", "vertex": "sprite.vert.glsl", "fragment": "orbit.frag.glsl" },
            { "name": "foo.bar", "vertex": "sprite.vert.glsl", "fragment": "orbit.frag.glsl" }
          ],
          "materials": []
        }
        """;

    private const string ShaderParameterCollisionManifest = """
        {
          "schemaVersion": 1,
          "shaders": [
            {
              "name": "runner.orbit",
              "vertex": "sprite.vert.glsl",
              "fragment": "orbit.frag.glsl"
            }
          ],
          "materials": [
            {
              "name": "runner.orbit.material",
              "shader": "runner.orbit",
              "uniforms": [
                { "name": "uGain", "type": "float", "default": 1.0 },
                { "name": "Gain", "type": "float", "default": 1.0 }
              ]
            }
          ]
        }
        """;
}
