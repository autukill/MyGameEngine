namespace GameEngine.Tools.AssetCompiler;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--generate-references")
            return GenerateReferences(args);
        if (args.Length > 0 && args[0] == "--validate-shaders")
            return ValidateShaders(args);

        if (args.Length is < 3 or > 4)
        {
            Console.Error.WriteLine(
                "Usage: GameEngineAssetCompiler [--incremental|--rebuild|--check] " +
                "<packages-root> <manifest-relative-path> <output-directory>");
            return 2;
        }

        try
        {
            int offset = args.Length == 4 ? 1 : 0;
            ContentBuildMode mode = args.Length == 4 ? ParseMode(args[0]) : ContentBuildMode.Incremental;
            var result = new ContentBuildPipeline().Build(new ContentBuildRequest(
                args[offset],
                args[offset + 1],
                args[offset + 2],
                mode));
            Console.WriteLine($"Build status: {result.Status}");
            Console.WriteLine($"Root package: {result.RootPackageId}");
            Console.WriteLine($"Manifest: {result.OutputManifestPath}");
            Console.WriteLine($"Packages: {result.PackageCount}");
            Console.WriteLine($"Built packages: {result.BuiltPackageCount}");
            Console.WriteLine($"Reused packages: {result.ReusedPackageCount}");
            Console.WriteLine($"Atlas pages: {result.AtlasPageCount}");
            Console.WriteLine($"Packed frames: {result.PackedFrameCount}");
            Console.WriteLine($"Passthrough frames: {result.PassthroughFrameCount}");
            Console.WriteLine($"Fingerprint: {result.InputFingerprint}");
            return result.Status == ContentBuildStatus.Stale ? 3 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int ValidateShaders(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: GameEngineAssetCompiler --validate-shaders <shaders.json>");
            return 2;
        }

        try
        {
            var loaded = GameEngine.Features.ShaderAssets.Infrastructure
                .ShaderAssetManifestLoader.Load(args[1]);
            Console.WriteLine($"Validated shader assets: {loaded.ManifestPath}");
            Console.WriteLine($"Shaders: {loaded.Manifest.Shaders.Count}");
            Console.WriteLine($"Materials: {loaded.Manifest.Materials.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int GenerateReferences(string[] args)
    {
        if (args.Length != 6)
        {
            Console.Error.WriteLine(
                "Usage: GameEngineAssetCompiler --generate-references " +
                "<compiled-packages-root> <manifest-relative-path> <output.cs> " +
                "<namespace> <root-class-name>");
            return 2;
        }

        try
        {
            ContentReferenceGenerationResult result = new ContentReferenceCodeGenerator().Generate(
                new ContentReferenceGenerationRequest(
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args[5]));
            Console.WriteLine($"Generated content references: {result.OutputFile}");
            Console.WriteLine($"Reference status: {(result.Changed ? "Updated" : "UpToDate")}");
            Console.WriteLine($"Packages: {result.PackageCount}");
            Console.WriteLine($"Textures: {result.TextureCount}");
            Console.WriteLine($"Sprites: {result.SpriteCount}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static ContentBuildMode ParseMode(string value) => value switch
    {
        "--incremental" => ContentBuildMode.Incremental,
        "--rebuild" => ContentBuildMode.Rebuild,
        "--check" => ContentBuildMode.Check,
        _ => throw new ArgumentException($"Unknown build mode '{value}'.")
    };
}
