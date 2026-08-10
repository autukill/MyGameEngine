namespace GameEngine.Features.ContentAssets.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Animation;
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
        IReadOnlyCollection<string> replacedAnimationNames,
        IReadOnlyList<TextureReplacementSource> textures,
        IReadOnlyList<SpriteReplacementSource> sprites,
        IReadOnlyList<AnimationReplacementSource> animations)
    {
        Owner = owner;
        Package = package;
        Revision = revision;
        BaseGeneration = baseGeneration;
        Packages = packages;
        ReplacedTextureNames = replacedTextureNames;
        ReplacedSpriteNames = replacedSpriteNames;
        ReplacedAnimationNames = replacedAnimationNames;
        Textures = textures;
        Sprites = sprites;
        Animations = animations;
    }

    public ContentPackageRef Package { get; }
    public CompiledContentRevision Revision { get; }
    public int TextureCount => Textures.Count;
    public int SpriteCount => Sprites.Count;
    public int AnimationCount => Animations.Count;
    internal long BaseGeneration { get; }
    internal ContentPackageManager Owner { get; }
    internal IReadOnlyList<PreparedPackageRevision> Packages { get; }
    internal IReadOnlyCollection<string> ReplacedTextureNames { get; }
    internal IReadOnlyCollection<string> ReplacedSpriteNames { get; }
    internal IReadOnlyCollection<string> ReplacedAnimationNames { get; }
    internal IReadOnlyList<TextureReplacementSource> Textures { get; }
    internal IReadOnlyList<SpriteReplacementSource> Sprites { get; }
    internal IReadOnlyList<AnimationReplacementSource> Animations { get; }
    internal int Consumed;
}

internal sealed record PreparedPackageRevision(
    string Id,
    string ManifestPath,
    TextureRef[] Textures,
    SpriteRef[] Sprites,
    AnimationClipRef[] Animations);
