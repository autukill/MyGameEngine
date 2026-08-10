namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Text.Json;
using GameEngine.Features.ContentAssets.Domain;

public static class CompiledContentRevisionReader
{
    public const string MetadataFileName = ".mygame-assets.json";
    private const string Owner = "MyGameEngine.AssetCompiler";

    internal sealed class Metadata
    {
        public int SchemaVersion { get; init; }
        public string Owner { get; init; } = string.Empty;
        public string CompilerVersion { get; init; } = string.Empty;
        public string RootPackageId { get; init; } = string.Empty;
        public string RootManifest { get; init; } = string.Empty;
        public string InputFingerprint { get; init; } = string.Empty;
    }

    public static CompiledContentRevision Read(string packagesRoot, ContentPackageRef expectedPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesRoot);
        string root = Path.GetFullPath(packagesRoot);
        string path = Path.Combine(root, MetadataFileName);
        using var stream = File.OpenRead(path);
        Metadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize(
                stream,
                CompiledContentRevisionJsonContext.Default.Metadata)
                ?? throw new InvalidDataException("Compiled content metadata is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Compiled content metadata is invalid JSON.", ex);
        }

        if (metadata.SchemaVersion != 1 || metadata.Owner != Owner)
            throw new InvalidDataException("Compiled content metadata has an unsupported owner or schema.");
        if (string.IsNullOrWhiteSpace(metadata.CompilerVersion) ||
            string.IsNullOrWhiteSpace(metadata.InputFingerprint))
            throw new InvalidDataException("Compiled content metadata has no stable revision fingerprint.");
        if (!StringComparer.Ordinal.Equals(metadata.RootPackageId, expectedPackage.Id) ||
            !StringComparer.Ordinal.Equals(
                Normalize(metadata.RootManifest),
                Normalize(expectedPackage.Manifest)))
        {
            throw new InvalidDataException(
                $"Compiled content metadata describes '{metadata.RootPackageId}:{metadata.RootManifest}', " +
                $"not '{expectedPackage.Id}:{expectedPackage.Manifest}'.");
        }

        return new CompiledContentRevision(
            metadata.RootPackageId,
            Normalize(metadata.RootManifest),
            metadata.InputFingerprint,
            metadata.CompilerVersion);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
