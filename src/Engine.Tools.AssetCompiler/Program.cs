namespace GameEngine.Tools.AssetCompiler;

internal static class Program
{
    private static int Main(string[] args)
    {
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

    private static ContentBuildMode ParseMode(string value) => value switch
    {
        "--incremental" => ContentBuildMode.Incremental,
        "--rebuild" => ContentBuildMode.Rebuild,
        "--check" => ContentBuildMode.Check,
        _ => throw new ArgumentException($"Unknown build mode '{value}'.")
    };
}
