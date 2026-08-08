namespace GameEngine.Tools.AssetCompiler;

public enum ContentBuildMode
{
    Incremental,
    Rebuild,
    Check
}

public enum ContentBuildStatus
{
    Built,
    UpToDate,
    Stale
}

public sealed record ContentBuildRequest(
    string PackagesRoot,
    string RootRelativeManifestPath,
    string OutputDirectory,
    ContentBuildMode Mode = ContentBuildMode.Incremental);

public sealed record ContentBuildResult(
    string RootPackageId,
    string OutputManifestPath,
    string InputFingerprint,
    ContentBuildStatus Status,
    int PackageCount,
    int BuiltPackageCount,
    int ReusedPackageCount,
    int AtlasPageCount,
    int PackedFrameCount,
    int PassthroughFrameCount);
