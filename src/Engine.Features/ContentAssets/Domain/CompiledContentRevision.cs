namespace GameEngine.Features.ContentAssets.Domain;

/// <summary>由内容编译器发布的、可稳定比较的完整输出修订。</summary>
public sealed record CompiledContentRevision(
    string PackageId,
    string RootManifest,
    string Fingerprint,
    string CompilerVersion);
