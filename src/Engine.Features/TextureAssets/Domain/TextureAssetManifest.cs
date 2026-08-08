namespace GameEngine.Features.TextureAssets.Domain;

/// <summary>A declarative texture entry relative to a content root.</summary>
public sealed record TextureAssetDefinition(
    string Name,
    string Path,
    TextureSampler Sampler);

/// <summary>An immutable set of texture assets to load as one assembly operation.</summary>
public sealed class TextureAssetManifest
{
    public TextureAssetManifest(IEnumerable<TextureAssetDefinition> textures)
    {
        ArgumentNullException.ThrowIfNull(textures);
        Textures = textures.ToArray();
        if (Textures.Count == 0)
            throw new ArgumentException("A texture manifest must contain at least one texture.", nameof(textures));
    }

    public IReadOnlyList<TextureAssetDefinition> Textures { get; }
}
