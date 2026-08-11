namespace AssetCompiler.Tests;

using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.Vorbis;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Tools.AssetCompiler;
using SkiaSharp;
using OggVorbisEncoder;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Asset Compiler Smoke Test ===\n");
        VerifyCompileAndRuntimeLoad();
        VerifyLosslessWebpAggregatePackage();
        VerifyTileWorldCompilation();
        VerifyShaderReferenceGeneration();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Asset Compiler smoke tests passed ==="
            : $"=== {_failures} Asset Compiler test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyTileWorldCompilation()
    {
        Console.WriteLine("5. Deterministic offline TileWorld LOD0 + visual LOD compilation");
        string workspace = Directory.CreateTempSubdirectory("mygame-tileworld-").FullName;
        string source = Path.Combine(workspace, "source");
        string output = Path.Combine(workspace, "compiled");
        string secondOutput = Path.Combine(workspace, "compiled-second");
        Directory.CreateDirectory(source);
        try
        {
            WriteSolid(Path.Combine(source, "tile.png"), 2, 2, SKColors.Green);
            File.WriteAllText(Path.Combine(source, "world.tilemap.json"), TileWorldMapManifest);
            File.WriteAllText(Path.Combine(source, "assets.json"), TileWorldPackageManifest);
            var pipeline = new ContentBuildPipeline();
            ContentBuildResult first = pipeline.Build(new ContentBuildRequest(
                source, "assets.json", output, ContentBuildMode.Incremental));
            ContentBuildResult cached = pipeline.Build(new ContentBuildRequest(
                source, "assets.json", output, ContentBuildMode.Incremental));
            ContentBuildResult repeated = pipeline.Build(new ContentBuildRequest(
                source, "assets.json", secondOutput, ContentBuildMode.Rebuild));

            string archivePath = Path.Combine(output, "world.mgworld");
            string compiledManifest = File.ReadAllText(Path.Combine(output, "assets.json"));
            Check(first.TileWorldCount == 1 && first.TileWorldChunkCount == 6 &&
                  first.TileWorldRasterChunkCount == 4 &&
                  cached.Status == ContentBuildStatus.UpToDate &&
                  cached.TileWorldRasterChunkCount == 4 && File.Exists(archivePath),
                "Pipeline compiles sparse LOD0 and power-of-two visual LOD Chunks and reuses an unchanged package");
            Check(repeated.TileWorldRasterChunkCount == 4 &&
                  File.ReadAllBytes(archivePath).SequenceEqual(
                      File.ReadAllBytes(Path.Combine(secondOutput, "world.mgworld"))),
                "Repeated visual LOD builds produce a byte-identical TileWorld archive");
            Check(compiledManifest.Contains("world.mgworld", StringComparison.Ordinal) &&
                  !compiledManifest.Contains("\"build\"", StringComparison.Ordinal) &&
                  !File.Exists(Path.Combine(output, "world.tilemap.json")),
                "Runtime manifest exposes only the compiled archive path");

            using (var archive = new TileWorldArchiveReader(File.OpenRead(archivePath)))
            {
                Check(archive.Metadata.Name == "compiler.world" &&
                      archive.Metadata.DeclaredLodCount == 3 &&
                      archive.Metadata.RasterSettings == new TileWorldRasterSettings(
                          256, 256, 2, TileWorldRasterSampling.PixelArt) &&
                      archive.Contains(new TileWorldChunkKey(0, -1, 0)) &&
                      archive.ReadChunk(new TileWorldChunkKey(0, 0, 0))
                          .Layers[0].CollisionRects.Length == 1,
                    "Compiled archive is runtime-readable and retains authoritative collision");
                TileWorldRasterChunkData raster = archive.ReadRasterChunk(
                    new TileWorldChunkKey(1, 0, 0));
                TileWorldRasterLayerData rasterLayer = raster.Layers.Single();
                DecodedImage decoded = new SkiaImageDecoder().Decode(
                    new MemoryStream(rasterLayer.EncodedBytes, writable: false));
                int greenPixel = ((2 + 32) * decoded.Width + 2 + 32) * 4;
                int transparentPixel = ((2 + 160) * decoded.Width + 2 + 160) * 4;
                Check(rasterLayer.Encoding == TileWorldRasterEncoding.WebpLossless &&
                      decoded.Width == 260 && decoded.Height == 260 &&
                      decoded.RgbaPixels[greenPixel] == 0 &&
                      decoded.RgbaPixels[greenPixel + 1] == 128 &&
                      decoded.RgbaPixels[greenPixel + 2] == 0 &&
                      decoded.RgbaPixels[greenPixel + 3] == 255 &&
                      decoded.RgbaPixels[transparentPixel + 3] == 0,
                    "Visual LOD stores a real lossless WebP with exact RGBA and transparent empty area");
            }

            var backend = new FakeTextureBackend();
            using (var textures = new TextureLibrary(backend))
            {
                var sprites = new SpriteLibrary(textures);
                using var manager = new ContentPackageManager(textures, sprites, output);
                using (LoadedContentPackage package = manager.Load("assets.json"))
                {
                    TileWorldRef world = package.GetTileWorld("compiler.world");
                    using TileWorldArchiveReader archive = manager.TileWorlds.Open(world);
                    Check(world == new TileWorldRef("compiler.world") &&
                          manager.TileWorlds.Count == 1 && archive.ChunkCount == 6,
                        "Content package exposes a borrowed TileWorld archive through a logical ref");
                }
                Check(manager.TileWorlds.Count == 0,
                    "Final package lease unregisters TileWorld before lower-level Tile assets");
            }

            string generated = Path.Combine(workspace, "generated", "GameEngine.Content.g.cs");
            ContentReferenceGenerationResult references = new ContentReferenceCodeGenerator().Generate(
                new ContentReferenceGenerationRequest(
                    output, "assets.json", generated, "Compiler.TileWorld.Content"));
            Check(references.TileWorldCount == 1 && File.ReadAllText(generated).Contains(
                    "TileWorldRef CompilerWorld", StringComparison.Ordinal),
                "Strongly typed TileWorldRef is generated from the runtime manifest");

            string firstFingerprint = first.InputFingerprint;
            File.WriteAllText(
                Path.Combine(source, "world.tilemap.json"),
                TileWorldMapManifest.Replace("[1, 0, 0, 0]", "[1, 1, 0, 0]", StringComparison.Ordinal));
            ContentBuildResult changed = pipeline.Build(new ContentBuildRequest(
                source, "assets.json", output, ContentBuildMode.Incremental));
            Check(changed.Status == ContentBuildStatus.Built &&
                  changed.InputFingerprint != firstFingerprint &&
                  changed.TileWorldChunkCount == 6 && changed.TileWorldRasterChunkCount == 4,
                "A TileWorld source edit invalidates its owning package fingerprint");

            byte[] validArchive = File.ReadAllBytes(archivePath);
            File.WriteAllText(
                Path.Combine(source, "assets.json"),
                TileWorldPackageManifest.Replace("webpLossless", "png", StringComparison.Ordinal));
            CheckThrows<InvalidDataException>(() => pipeline.Build(new ContentBuildRequest(
                    source, "assets.json", output, ContentBuildMode.Rebuild)),
                "Visual TileWorld LODs reject non-WebP encoding before replacing valid output");
            Check(File.ReadAllBytes(archivePath).SequenceEqual(validArchive),
                "Rejected visual LOD configuration preserves the previous valid archive");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static void VerifyLosslessWebpAggregatePackage()
    {
        Console.WriteLine("4. Lossless WebP Atlas and dependency-only aggregate package");
        string workspace = Directory.CreateTempSubdirectory("mygame-webp-atlas-").FullName;
        string source = Path.Combine(workspace, "source");
        string home = Path.Combine(source, "Home");
        string firstOutput = Path.Combine(workspace, "compiled-a");
        string secondOutput = Path.Combine(workspace, "compiled-b");
        Directory.CreateDirectory(home);
        try
        {
            string sourceImage = Path.Combine(home, "sheet.png");
            WriteExactAlphaFixture(sourceImage);
            File.WriteAllText(Path.Combine(source, "assets.json"), AggregateManifest);
            File.WriteAllText(Path.Combine(home, "assets.json"), WebpHomeManifest);

            var pipeline = new ContentBuildPipeline();
            ContentBuildResult first = pipeline.Build(new ContentBuildRequest(
                source,
                "assets.json",
                firstOutput));
            ContentBuildResult second = pipeline.Build(new ContentBuildRequest(
                source,
                "assets.json",
                secondOutput));

            string webpPage = Path.Combine(
                firstOutput,
                "Home",
                "atlas",
                "pixel-art-0.webp");
            byte[] encoded = File.ReadAllBytes(webpPage);
            Check(first.PackageCount == 2 && first.AtlasPageCount == 1 &&
                  first.PackedFrameCount == 1 && first.PassthroughFrameCount == 0,
                "Dependency-only root compiles its Home package Atlas");
            Check(encoded.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                  encoded.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                "WebP Atlas page has the RIFF/WEBP signature and extension");
            Check(DirectoriesEqual(firstOutput, secondOutput),
                "Repeated lossless WebP graph builds are byte-identical");

            var decoder = new SkiaImageDecoder();
            using var sourceStream = File.OpenRead(sourceImage);
            using var pageStream = File.OpenRead(webpPage);
            DecodedImage sourcePixels = decoder.Decode(sourceStream);
            DecodedImage pagePixels = decoder.Decode(pageStream);
            Check(sourcePixels.RgbaPixels.AsSpan(0, 4).SequenceEqual(
                    new byte[] { 17, 34, 51, 0 }),
                "WebP fixture contains non-zero hidden RGB under transparent alpha");
            Check(sourcePixels.Width == pagePixels.Width &&
                  sourcePixels.Height == pagePixels.Height &&
                  sourcePixels.RgbaPixels.SequenceEqual(pagePixels.RgbaPixels),
                "Lossless WebP Atlas preserves unpremultiplied RGBA pixels");

            string generated = Path.Combine(workspace, "generated", "GameEngine.Content.g.cs");
            ContentReferenceGenerationResult references = new ContentReferenceCodeGenerator().Generate(
                new ContentReferenceGenerationRequest(
                    firstOutput,
                    "assets.json",
                    generated,
                    "Compiler.Webp.Content"));
            Check(references.PackageCount == 2 && references.SpriteCount == 1 &&
                  File.ReadAllText(generated).Contains(
                      "ContentPackageRef WebpHome",
                      StringComparison.Ordinal),
                "Strong references include assets from the aggregate dependency graph");

            var backend = new FakeTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, firstOutput);
            using var package = manager.Load("assets.json");
            Check(package.GetSprite("webp.home.sheet").Name == "webp.home.sheet" &&
                  textures.Count == 1 && sprites.Count == 1,
                "Root package lease exposes and owns the Home dependency Sprite");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
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
            File.WriteAllBytes(Path.Combine(source, "shot.wav"), CreatePcm16Wave());
            File.WriteAllBytes(Path.Combine(source, "music.ogg"), CreateVorbisOgg());
            File.WriteAllText(Path.Combine(source, "level.tilemap.json"), TileMapManifest);
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
                  compiledJson.Contains("shot.wav", StringComparison.Ordinal) &&
                  compiledJson.Contains("music.ogg", StringComparison.Ordinal) &&
                  compiledJson.Contains("atlas/pixel-art-0.png", StringComparison.Ordinal) &&
                  compiledJson.Contains("compiler.grid.run", StringComparison.Ordinal),
                "Atlas remap preserves declarative Animation assets");
            Check(File.Exists(Path.Combine(firstOutput, "shared", "assets.json")) &&
                  File.Exists(Path.Combine(firstOutput, "shared", "white.png")),
                "Dependency packages are copied into the compiled packages root");
            Check(File.Exists(Path.Combine(firstOutput, "shot.wav")),
                "Audio assets are copied through the Atlas compiler boundary");
            Check(File.Exists(Path.Combine(firstOutput, "music.ogg")),
                "Streaming OGG is preserved as compressed package content");
            Check(File.Exists(Path.Combine(firstOutput, "level.tilemap.json")),
                "TileMap documents are copied through the Atlas compiler boundary");

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
            AudioClipDescriptor audio = manager.Audio.Get(package.GetAudioClip("compiler.shot"));
            Check(audio.Decoded is { Channels: 1, SampleRate: 48_000 },
                "Compiled package retains a runtime-decodable Audio clip");
            AudioClipDescriptor music = manager.Audio.Get(package.GetAudioClip("compiler.music"));
            Check(music.StorageKind == AudioClipStorageKind.Streaming &&
                  music.StreamFactory is VorbisAudioStreamFactory && music.Decoded is null,
                "Compiled package retains a lazy streaming OGG factory");
            Check(package.GetTileSet("compiler.tiles") == new TileSetRef("compiler.tiles") &&
                  manager.TileMaps.Get(package.GetTileMap("compiler.level"))
                      .GetLayer("ground").GetCell(0, 0).Tile == new TileId(1),
                "Compiled package retains TileSet and external TileMap assets");

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
              first.AnimationCount == 1 && first.AudioClipCount == 2 && first.AnimationEventCount == 1 &&
              first.TileSetCount == 1 && first.TileMapCount == 1 &&
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
        Check(source.Contains("AudioClipRef CompilerShot", StringComparison.Ordinal) &&
              source.Contains("AudioClipRef CompilerMusic", StringComparison.Ordinal),
            "Audio clips become strongly typed logical references");
        Check(source.Contains("TileSetRef CompilerTiles", StringComparison.Ordinal) &&
              source.Contains("TileMapRef CompilerLevel", StringComparison.Ordinal),
            "TileSet and TileMap assets become strongly typed logical references");
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

        string tileMapPath = Path.Combine(source, "level.tilemap.json");
        File.WriteAllText(tileMapPath, TileMapManifest.Replace("[1, 0, 0, 0]", "[1, 1, 0, 0]"));
        ContentBuildResult tileMapStale = pipeline.Build(request with { Mode = ContentBuildMode.Check });
        ContentBuildResult tileMapRebuilt = pipeline.Build(request);
        Check(tileMapStale.Status == ContentBuildStatus.Stale &&
              tileMapRebuilt.BuiltPackageCount == 1 &&
              File.ReadAllText(Path.Combine(output, "level.tilemap.json"))
                  .Contains("[1, 1, 0, 0]", StringComparison.Ordinal),
            "TileMap edits participate in the incremental fingerprint and copied output");

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
        Console.WriteLine("5. Strongly typed Shader, Material, and parameter references");
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

    private static void WriteExactAlphaFixture(string path)
    {
        var info = new SKImageInfo(4, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        byte[] pixels =
        [
            17, 34, 51, 0,
            255, 0, 0, 255,
            0, 255, 0, 128,
            0, 0, 255, 255,
            255, 255, 0, 255,
            0, 255, 255, 255,
            255, 0, 255, 255,
            255, 255, 255, 255
        ];
        System.Runtime.InteropServices.Marshal.Copy(
            pixels,
            0,
            bitmap.GetPixels(),
            pixels.Length);
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

    private static byte[] CreatePcm16Wave()
    {
        const short channels = 1;
        const int sampleRate = 48_000;
        const int frames = 480;
        int dataLength = frames * sizeof(short);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
        }
        return stream.ToArray();
    }

    private static byte[] CreateVorbisOgg()
    {
        const int channels = 2;
        const int sampleRate = 44_100;
        const int frameCount = 2_205;
        var samples = new float[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            samples[channel] = new float[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
                samples[channel][frame] = 0.15f * MathF.Sin(2f * MathF.PI * 220f * frame / sampleRate);
        }

        using var output = new MemoryStream();
        VorbisInfo info = VorbisInfo.InitVariableBitRate(channels, sampleRate, 0.3f);
        var ogg = new OggStream(0x4D4746);
        var comments = new Comments();
        comments.AddTag("ENCODER", "MyGameEngine.AssetCompiler.Tests");
        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushOggPages(ogg, output, force: true);

        ProcessingState state = ProcessingState.Create(info);
        const int blockFrames = 512;
        for (var offset = 0; offset < frameCount; offset += blockFrames)
        {
            int length = Math.Min(blockFrames, frameCount - offset);
            state.WriteData(samples, length, offset);
            while (!ogg.Finished && state.PacketOut(out OggPacket packet))
            {
                ogg.PacketIn(packet);
                FlushOggPages(ogg, output, force: false);
            }
        }
        state.WriteEndOfStream();
        while (!ogg.Finished && state.PacketOut(out OggPacket packet))
        {
            ogg.PacketIn(packet);
            FlushOggPages(ogg, output, force: false);
        }
        FlushOggPages(ogg, output, force: true);
        return output.ToArray();
    }

    private static void FlushOggPages(OggStream ogg, Stream output, bool force)
    {
        while (ogg.PageOut(out OggPage page, force))
        {
            output.Write(page.Header);
            output.Write(page.Body);
        }
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
          ],
          "audioClips": [
            { "name": "compiler.shot", "path": "shot.wav", "streaming": false },
            { "name": "compiler.music", "path": "music.ogg", "streaming": true }
          ],
          "tileSets": [{
            "name": "compiler.tiles",
            "tileSize": { "width": 2, "height": 2 },
            "tiles": [{ "id": 1, "sprite": "compiler.grid", "subImage": 0, "collision": "solid" }]
          }],
          "tileMaps": [
            { "name": "compiler.level", "path": "level.tilemap.json" }
          ]
        }
        """;

    private const string TileMapManifest = """
        {
          "schemaVersion": 1,
          "name": "compiler.level",
          "chunkSize": { "width": 2, "height": 2 },
          "layers": [{
            "name": "ground",
            "tileSet": "compiler.tiles",
            "chunks": [{ "x": 0, "y": 0, "tiles": [1, 0, 0, 0] }]
          }]
        }
        """;

    private const string TileWorldPackageManifest = """
        {
          "schemaVersion": 1,
          "id": "compiler.tileworld.assets",
          "dependencies": [],
          "atlas": {
            "maxPageSize": { "width": 4, "height": 4 },
            "padding": 0,
            "extrude": 0,
            "textures": ["compiler.world.texture"]
          },
          "textures": [
            { "name": "compiler.world.texture", "path": "tile.png", "sampling": "pixelArt" }
          ],
          "sprites": [{
            "name": "compiler.world.tile", "layout": "single",
            "texture": "compiler.world.texture", "origin": { "x": 0, "y": 0 }
          }],
          "tileSets": [{
            "name": "compiler.world.tiles",
            "tileSize": { "width": 2, "height": 2 },
            "tiles": [{
              "id": 1, "sprite": "compiler.world.tile", "collision": "solid"
            }]
          }],
          "tileWorlds": [{
            "name": "compiler.world",
            "path": "world.tilemap.json",
            "build": {
              "bounds": { "minX": -1, "minY": 0, "maxX": 1, "maxY": 1 },
              "lodCount": 3,
              "rasterChunkSize": { "width": 256, "height": 256 },
              "encoding": "webpLossless",
              "sampling": "pixelArt",
              "gutter": 2
            }
          }]
        }
        """;

    private const string TileWorldMapManifest = """
        {
          "schemaVersion": 1,
          "name": "compiler.world",
          "chunkSize": { "width": 2, "height": 2 },
          "layers": [{
            "name": "ground",
            "tileSet": "compiler.world.tiles",
            "chunks": [
              { "x": -1, "y": 0, "tiles": [1, 0, 0, 0] },
              { "x": 0, "y": 0, "tiles": [1, 0, 0, 0] }
            ]
          }]
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

    private const string AggregateManifest = """
        {
          "schemaVersion": 1,
          "id": "webp.root",
          "dependencies": [
            { "id": "webp.home", "manifest": "Home/assets.json" }
          ],
          "textures": [],
          "sprites": []
        }
        """;

    private const string WebpHomeManifest = """
        {
          "schemaVersion": 1,
          "id": "webp.home",
          "dependencies": [],
          "atlas": {
            "pageEncoding": "webpLossless",
            "maxPageSize": { "width": 8, "height": 8 },
            "padding": 0,
            "extrude": 0,
            "textures": ["webp.home.sheet.source"]
          },
          "textures": [
            {
              "name": "webp.home.sheet.source",
              "path": "sheet.png",
              "sampling": "pixelArt"
            }
          ],
          "sprites": [
            {
              "name": "webp.home.sheet",
              "layout": "single",
              "texture": "webp.home.sheet.source",
              "origin": { "x": 0, "y": 0 }
            }
          ]
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
