namespace GameEngine.Features.ContentAssets.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.TextureAssets.Domain;

public sealed record AssetPackageDependency(
    string Id,
    string Manifest);

public enum SpriteAssetLayout
{
    Single,
    Grid,
    Frames
}

public readonly record struct PixelSizeI(int Width, int Height);

public sealed record AtlasAssetBuildDefinition(
    PixelSizeI MaxPageSize,
    int Padding,
    int Extrude,
    IReadOnlyList<string> Textures);

public sealed record SpriteAssetFrameDefinition(
    string? TextureName,
    PixelRectI? SourceRect);

public sealed record SpriteAssetDefinition(
    string Name,
    string? TextureName,
    SpriteAssetLayout Layout,
    PixelRectI? SourceRect,
    PixelSizeI? FrameSize,
    int? FrameCount,
    IReadOnlyList<SpriteAssetFrameDefinition> Frames,
    Vector2? LogicalSize,
    Vector2 Origin,
    float FramesPerSecond);

public sealed record AnimationAssetMarkerDefinition(
    int Frame,
    string Event);

public sealed record AnimationAssetDefinition(
    string Name,
    string SpriteName,
    IReadOnlyList<int> Frames,
    float FramesPerSecond,
    AnimationLoopMode LoopMode,
    IReadOnlyList<AnimationAssetMarkerDefinition> Markers);

public sealed record AudioAssetDefinition(
    string Name,
    string Path,
    bool Streaming);

public sealed class AssetPackageManifest
{
    public const int CurrentSchemaVersion = 1;

    public AssetPackageManifest(
        int schemaVersion,
        string id,
        IEnumerable<AssetPackageDependency> dependencies,
        IEnumerable<TextureAssetDefinition> textures,
        IEnumerable<SpriteAssetDefinition> sprites,
        IEnumerable<AnimationAssetDefinition> animations,
        IEnumerable<AudioAssetDefinition> audioClips,
        AtlasAssetBuildDefinition? atlas = null)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        Dependencies = dependencies.ToArray();
        Textures = textures.ToArray();
        Sprites = sprites.ToArray();
        Animations = animations.ToArray();
        AudioClips = audioClips.ToArray();
        Atlas = atlas;
    }

    public int SchemaVersion { get; }
    public string Id { get; }
    public IReadOnlyList<AssetPackageDependency> Dependencies { get; }
    public IReadOnlyList<TextureAssetDefinition> Textures { get; }
    public IReadOnlyList<SpriteAssetDefinition> Sprites { get; }
    public IReadOnlyList<AnimationAssetDefinition> Animations { get; }
    public IReadOnlyList<AudioAssetDefinition> AudioClips { get; }
    public AtlasAssetBuildDefinition? Atlas { get; }
}
