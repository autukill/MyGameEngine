namespace GameEngine.Features.TextureAssets.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;

/// <summary>Parses and atomically loads JSON texture manifests from a constrained content root.</summary>
public static class TextureManifestLoader
{
    public static TextureAssetManifest Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!json.CanRead)
            throw new ArgumentException("The manifest stream must be readable.", nameof(json));

        var document = JsonSerializer.Deserialize(
            json,
            TextureManifestJsonContext.Default.ManifestDocument)
            ?? throw new InvalidDataException("The texture manifest is empty.");
        if (document.Textures is null || document.Textures.Count == 0)
            throw new InvalidDataException("The texture manifest must contain at least one texture.");

        var definitions = new TextureAssetDefinition[document.Textures.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < document.Textures.Count; i++)
        {
            var entry = document.Textures[i]
                ?? throw new InvalidDataException($"Texture entry {i} is null.");
            if (string.IsNullOrWhiteSpace(entry.Name))
                throw new InvalidDataException($"Texture entry {i} has no name.");
            if (!names.Add(entry.Name))
                throw new InvalidDataException($"Texture '{entry.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(entry.Path))
                throw new InvalidDataException($"Texture '{entry.Name}' has no path.");

            definitions[i] = new TextureAssetDefinition(
                entry.Name,
                entry.Path,
                ParseSampler(entry.Sampling));
        }

        return new TextureAssetManifest(definitions);
    }

    public static IReadOnlyList<TextureRef> LoadInto(
        TextureLibrary library,
        TextureAssetManifest manifest,
        string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        string root = Path.GetFullPath(contentRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Content root '{root}' does not exist.");

        var loaded = new List<TextureRef>(manifest.Textures.Count);
        try
        {
            foreach (var definition in manifest.Textures)
            {
                string path = ResolveAssetPath(root, definition.Path);
                loaded.Add(library.Load(
                    definition.Name,
                    path,
                    definition.Sampler));
            }

            return loaded;
        }
        catch
        {
            foreach (var texture in loaded)
                library.Remove(texture);
            throw;
        }
    }

    private static string ResolveAssetPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Texture manifest paths must be relative to the content root.");

        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                $"Texture path '{relativePath}' escapes the configured content root.");
        }

        return fullPath;
    }

    private static TextureSampler ParseSampler(string? sampling) =>
        sampling?.Trim().ToLowerInvariant() switch
        {
            null or "" or "smooth" => TextureSampler.Smooth,
            "pixelart" or "pixel-art" or "nearest" => TextureSampler.PixelArt,
            _ => throw new InvalidDataException($"Unknown texture sampling preset '{sampling}'.")
        };

    internal sealed class ManifestDocument
    {
        public List<ManifestEntry?>? Textures { get; init; }
    }

    internal sealed class ManifestEntry
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
        public string? Sampling { get; init; }
    }
}
