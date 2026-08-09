namespace GameEngine.Features.ContentAssets.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Sprites.Domain;
using GameEngine.Features.TextureAssets.Domain;

public sealed class PreparedContentPackageReload
{
    internal PreparedContentPackageReload(
        ContentPackageManager owner,
        ContentPackageRef package,
        CompiledContentRevision revision,
        long baseGeneration,
        IReadOnlyList<PreparedPackageRevision> packages,
        IReadOnlyCollection<string> replacedTextureNames,
        IReadOnlyCollection<string> replacedSpriteNames,
        IReadOnlyList<TextureReplacementSource> textures,
        IReadOnlyList<SpriteReplacementSource> sprites)
    {
        Owner = owner;
        Package = package;
        Revision = revision;
        BaseGeneration = baseGeneration;
        Packages = packages;
        ReplacedTextureNames = replacedTextureNames;
        ReplacedSpriteNames = replacedSpriteNames;
        Textures = textures;
        Sprites = sprites;
    }

    public ContentPackageRef Package { get; }
    public CompiledContentRevision Revision { get; }
    public int TextureCount => Textures.Count;
    public int SpriteCount => Sprites.Count;
    internal long BaseGeneration { get; }
    internal ContentPackageManager Owner { get; }
    internal IReadOnlyList<PreparedPackageRevision> Packages { get; }
    internal IReadOnlyCollection<string> ReplacedTextureNames { get; }
    internal IReadOnlyCollection<string> ReplacedSpriteNames { get; }
    internal IReadOnlyList<TextureReplacementSource> Textures { get; }
    internal IReadOnlyList<SpriteReplacementSource> Sprites { get; }
    internal int Consumed;
}

internal sealed record PreparedPackageRevision(
    string Id,
    string ManifestPath,
    TextureRef[] Textures,
    SpriteRef[] Sprites);
