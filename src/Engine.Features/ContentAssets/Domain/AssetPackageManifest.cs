namespace GameEngine.Features.ContentAssets.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;
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

public enum AtlasPageEncoding
{
    Png,
    WebpLossless
}

public sealed record AtlasAssetBuildDefinition(
    PixelSizeI MaxPageSize,
    int Padding,
    int Extrude,
    AtlasPageEncoding PageEncoding,
    IReadOnlyList<string> Textures)
{
    public AtlasAssetBuildDefinition(
        PixelSizeI maxPageSize,
        int padding,
        int extrude,
        IReadOnlyList<string> textures)
        : this(maxPageSize, padding, extrude, AtlasPageEncoding.Png, textures)
    {
    }
}

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

public sealed record TileAssetDefinition(
    ushort Id,
    string SpriteName,
    int SubImage,
    TileCollisionKind Collision);

public sealed record TileSetAssetDefinition(
    string Name,
    Vector2 TileSize,
    IReadOnlyList<TileAssetDefinition> Tiles);

public sealed record TileMapAssetDefinition(
    string Name,
    string Path);

public sealed record TileWorldAssetBuildDefinition(
    TileWorldChunkBounds Bounds,
    int LodCount,
    PixelSizeI RasterChunkSize,
    AtlasPageEncoding Encoding,
    TextureSampler Sampling,
    int Gutter,
    IReadOnlyList<TileWorldFallbackSurfaceAssetDefinition> FallbackSurfaces)
{
    public TileWorldAssetBuildDefinition(
        TileWorldChunkBounds bounds,
        int lodCount,
        PixelSizeI rasterChunkSize,
        AtlasPageEncoding encoding,
        TextureSampler sampling,
        int gutter)
        : this(bounds, lodCount, rasterChunkSize, encoding, sampling, gutter, [])
    {
    }
}

public sealed record TileWorldFallbackSurfaceAssetDefinition(
    string Layer,
    string Path,
    TextureSampler Sampling);

public sealed record TileWorldAssetDefinition(
    string Name,
    string Path,
    TileWorldAssetBuildDefinition? Build);

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
        AtlasAssetBuildDefinition? atlas = null,
        IEnumerable<TileSetAssetDefinition>? tileSets = null,
        IEnumerable<TileMapAssetDefinition>? tileMaps = null,
        IEnumerable<TileWorldAssetDefinition>? tileWorlds = null)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        Dependencies = dependencies.ToArray();
        Textures = textures.ToArray();
        Sprites = sprites.ToArray();
        Animations = animations.ToArray();
        AudioClips = audioClips.ToArray();
        TileSets = tileSets?.ToArray() ?? [];
        TileMaps = tileMaps?.ToArray() ?? [];
        TileWorlds = tileWorlds?.ToArray() ?? [];
        Atlas = atlas;
    }

    public int SchemaVersion { get; }
    public string Id { get; }
    public IReadOnlyList<AssetPackageDependency> Dependencies { get; }
    public IReadOnlyList<TextureAssetDefinition> Textures { get; }
    public IReadOnlyList<SpriteAssetDefinition> Sprites { get; }
    public IReadOnlyList<AnimationAssetDefinition> Animations { get; }
    public IReadOnlyList<AudioAssetDefinition> AudioClips { get; }
    public IReadOnlyList<TileSetAssetDefinition> TileSets { get; }
    public IReadOnlyList<TileMapAssetDefinition> TileMaps { get; }
    public IReadOnlyList<TileWorldAssetDefinition> TileWorlds { get; }
    public AtlasAssetBuildDefinition? Atlas { get; }
}
