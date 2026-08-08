namespace GameEngine.Tools.AssetCompiler;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine(
                "Usage: GameEngineAssetCompiler <packages-root> <manifest-relative-path> <output-directory>");
            return 2;
        }

        try
        {
            var result = new ContentAssetCompiler().Compile(args[0], args[1], args[2]);
            Console.WriteLine($"Compiled package: {result.PackageId}");
            Console.WriteLine($"Manifest: {result.OutputManifestPath}");
            Console.WriteLine($"Atlas pages: {result.AtlasPageCount}");
            Console.WriteLine($"Packed frames: {result.PackedFrameCount}");
            Console.WriteLine($"Passthrough frames: {result.PassthroughFrameCount}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
