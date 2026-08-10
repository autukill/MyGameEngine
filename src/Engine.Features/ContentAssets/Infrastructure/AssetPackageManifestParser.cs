namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Animation;
using GameEngine.Features.TextureAssets.Domain;

public static class AssetPackageManifestParser
{
    public static AssetPackageManifest Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!json.CanRead)
            throw new ArgumentException("The manifest stream must be readable.", nameof(json));

        ManifestDto document;
        try
        {
            document = JsonSerializer.Deserialize(
                json,
                AssetPackageManifestJsonContext.Default.ManifestDto)
                ?? throw new InvalidDataException("The asset package manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The asset package manifest is invalid JSON.", ex);
        }

        if (document.SchemaVersion != AssetPackageManifest.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Unsupported schemaVersion '{document.SchemaVersion}'. Expected {AssetPackageManifest.CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(document.Id))
            throw new InvalidDataException("Package id cannot be empty.");

        var dependencies = ParseDependencies(document.Dependencies);
        var textures = ParseTextures(document.Textures);
        var sprites = ParseSprites(document.Sprites);
        var animations = ParseAnimations(document.Animations);
        var audioClips = ParseAudioClips(document.AudioClips);
        var atlas = ParseAtlas(document.Atlas, textures);
        if (textures.Count == 0 && sprites.Count == 0 && animations.Count == 0 && audioClips.Count == 0)
            throw new InvalidDataException("An asset package must contain at least one Texture, Sprite, Animation or Audio clip.");

        return new AssetPackageManifest(
            document.SchemaVersion,
            document.Id,
            dependencies,
            textures,
            sprites,
            animations,
            audioClips,
            atlas);
    }

    private static IReadOnlyList<AudioAssetDefinition> ParseAudioClips(List<AudioClipDto?>? source)
    {
        if (source is null) return Array.Empty<AudioAssetDefinition>();
        var result = new AudioAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            AudioClipDto item = source[i]
                ?? throw new InvalidDataException($"Audio clip entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"Audio clip entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"Audio clip '{item.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException($"Audio clip '{item.Name}' has no path.");
            result[i] = new AudioAssetDefinition(item.Name, item.Path, item.Streaming ?? false);
        }
        return result;
    }

    private static IReadOnlyList<AnimationAssetDefinition> ParseAnimations(
        List<AnimationDto?>? source)
    {
        if (source is null) return Array.Empty<AnimationAssetDefinition>();
        var result = new AnimationAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            AnimationDto item = source[i]
                ?? throw new InvalidDataException($"Animation entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"Animation entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"Animation '{item.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Sprite))
                throw new InvalidDataException($"Animation '{item.Name}' has no Sprite.");
            if (item.Frames is not { Count: > 0 })
                throw new InvalidDataException($"Animation '{item.Name}' requires at least one frame.");
            for (int frame = 0; frame < item.Frames.Count; frame++)
            {
                if (item.Frames[frame] < 0)
                    throw new InvalidDataException($"Animation '{item.Name}' contains a negative Sprite frame.");
            }
            float fps = item.FramesPerSecond ?? 0f;
            if (!float.IsFinite(fps) || fps <= 0f)
                throw new InvalidDataException($"Animation '{item.Name}' framesPerSecond must be finite and positive.");
            AnimationLoopMode loopMode = item.Loop?.Trim().ToLowerInvariant() switch
            {
                null or "" or "loop" => AnimationLoopMode.Loop,
                "once" => AnimationLoopMode.Once,
                "pingpong" or "ping-pong" => AnimationLoopMode.PingPong,
                _ => throw new InvalidDataException($"Animation '{item.Name}' has unknown loop mode '{item.Loop}'.")
            };

            List<AnimationMarkerDto?> markers = item.Markers ?? [];
            var parsedMarkers = new AnimationAssetMarkerDefinition[markers.Count];
            var markerKeys = new HashSet<(int Frame, string Event)>();
            for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
            {
                AnimationMarkerDto marker = markers[markerIndex]
                    ?? throw new InvalidDataException($"Marker {markerIndex} of Animation '{item.Name}' is null.");
                if ((uint)marker.Frame >= (uint)item.Frames.Count)
                    throw new InvalidDataException($"Marker {markerIndex} of Animation '{item.Name}' is outside its frame list.");
                if (string.IsNullOrWhiteSpace(marker.Event))
                    throw new InvalidDataException($"Marker {markerIndex} of Animation '{item.Name}' has no event name.");
                if (!markerKeys.Add((marker.Frame, marker.Event)))
                    throw new InvalidDataException(
                        $"Animation '{item.Name}' repeats event '{marker.Event}' on frame {marker.Frame}.");
                parsedMarkers[markerIndex] = new AnimationAssetMarkerDefinition(marker.Frame, marker.Event);
            }

            result[i] = new AnimationAssetDefinition(
                item.Name,
                item.Sprite,
                item.Frames.ToArray(),
                fps,
                loopMode,
                parsedMarkers);
        }
        return result;
    }

    private static AtlasAssetBuildDefinition? ParseAtlas(
        AtlasDto? source,
        IReadOnlyList<TextureAssetDefinition> textures)
    {
        if (source is null) return null;
        if (source.Textures is null || source.Textures.Count == 0)
            throw new InvalidDataException("Atlas build configuration requires at least one Texture name.");

        PixelSizeI maxPageSize = source.MaxPageSize is null
            ? new PixelSizeI(2048, 2048)
            : ParsePositiveSize(source.MaxPageSize, "Atlas maxPageSize");
        int padding = source.Padding ?? 1;
        int extrude = source.Extrude ?? 1;
        if (padding < 0 || extrude < 0)
            throw new InvalidDataException("Atlas padding and extrude must be non-negative.");

        var localTextures = textures.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var selected = new string[source.Textures.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Textures.Count; i++)
        {
            string? name = source.Textures[i];
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException($"Atlas Texture entry {i} is empty.");
            if (!names.Add(name))
                throw new InvalidDataException($"Atlas Texture '{name}' appears more than once.");
            if (!localTextures.Contains(name))
                throw new InvalidDataException($"Atlas Texture '{name}' is not declared by this package.");
            selected[i] = name;
        }

        return new AtlasAssetBuildDefinition(maxPageSize, padding, extrude, selected);
    }

    private static IReadOnlyList<AssetPackageDependency> ParseDependencies(
        List<DependencyDto?>? source)
    {
        if (source is null) return Array.Empty<AssetPackageDependency>();
        var result = new AssetPackageDependency[source.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i]
                ?? throw new InvalidDataException($"Dependency entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Id))
                throw new InvalidDataException($"Dependency entry {i} has no id.");
            if (!ids.Add(item.Id))
                throw new InvalidDataException($"Dependency '{item.Id}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Manifest))
                throw new InvalidDataException($"Dependency '{item.Id}' has no manifest path.");
            result[i] = new AssetPackageDependency(item.Id, item.Manifest);
        }
        return result;
    }

    private static IReadOnlyList<TextureAssetDefinition> ParseTextures(List<TextureDto?>? source)
    {
        if (source is null) return Array.Empty<TextureAssetDefinition>();
        var result = new TextureAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i]
                ?? throw new InvalidDataException($"Texture entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"Texture entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"Texture '{item.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException($"Texture '{item.Name}' has no path.");
            result[i] = new TextureAssetDefinition(item.Name, item.Path, ParseSampler(item.Sampling));
        }
        return result;
    }

    private static IReadOnlyList<SpriteAssetDefinition> ParseSprites(List<SpriteDto?>? source)
    {
        if (source is null) return Array.Empty<SpriteAssetDefinition>();
        var result = new SpriteAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i]
                ?? throw new InvalidDataException($"Sprite entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"Sprite entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"Sprite '{item.Name}' appears more than once.");
            if (item.Origin is null)
                throw new InvalidDataException($"Sprite '{item.Name}' has no origin.");
            if (item.Origin.X is null || item.Origin.Y is null)
                throw new InvalidDataException($"Sprite '{item.Name}' origin requires x and y.");

            var layout = ParseLayout(item.Layout, item.Name);
            Vector2? logicalSize = item.Size is null ? null : ParseLogicalSize(item.Size, item.Name);
            var origin = new Vector2(item.Origin.X.Value, item.Origin.Y.Value);
            if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
                throw new InvalidDataException($"Sprite '{item.Name}' origin must be finite.");
            float fps = item.FramesPerSecond ?? 0f;
            if (!float.IsFinite(fps) || fps < 0f)
                throw new InvalidDataException($"Sprite '{item.Name}' framesPerSecond must be finite and non-negative.");

            PixelRectI? sourceRect = item.Source is null ? null : ParseRect(item.Source, item.Name);
            PixelSizeI? frameSize = item.FrameSize is null ? null : ParsePixelSize(item.FrameSize, item.Name);
            var frames = ParseFrames(item.Frames, item.Name);
            ValidateLayoutFields(item, layout, sourceRect, frameSize, frames);

            result[i] = new SpriteAssetDefinition(
                item.Name,
                item.Texture,
                layout,
                sourceRect,
                frameSize,
                item.FrameCount,
                frames,
                logicalSize,
                origin,
                fps);
        }
        return result;
    }

    private static void ValidateLayoutFields(
        SpriteDto item,
        SpriteAssetLayout layout,
        PixelRectI? source,
        PixelSizeI? frameSize,
        IReadOnlyList<SpriteAssetFrameDefinition> frames)
    {
        switch (layout)
        {
            case SpriteAssetLayout.Single:
                RequireTexture(item);
                if (frameSize is not null || item.FrameCount is not null || item.Frames is not null)
                    throw new InvalidDataException($"Single Sprite '{item.Name}' contains Grid/Frames fields.");
                break;
            case SpriteAssetLayout.Grid:
                RequireTexture(item);
                if (frameSize is null || item.FrameCount is null or <= 0)
                    throw new InvalidDataException($"Grid Sprite '{item.Name}' requires frameSize and positive frameCount.");
                if (source is not null || item.Frames is not null)
                    throw new InvalidDataException($"Grid Sprite '{item.Name}' contains Single/Frames fields.");
                break;
            case SpriteAssetLayout.Frames:
                if (source is not null || frameSize is not null || item.FrameCount is not null)
                    throw new InvalidDataException($"Frames Sprite '{item.Name}' contains Single/Grid fields.");
                if (frames.Count == 0)
                    throw new InvalidDataException($"Frames Sprite '{item.Name}' must contain at least one frame.");
                for (int i = 0; i < frames.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(frames[i].TextureName) && string.IsNullOrWhiteSpace(item.Texture))
                        throw new InvalidDataException($"Frame {i} of Sprite '{item.Name}' has no Texture.");
                }
                break;
        }
    }

    private static void RequireTexture(SpriteDto item)
    {
        if (string.IsNullOrWhiteSpace(item.Texture))
            throw new InvalidDataException($"Sprite '{item.Name}' requires a Texture.");
    }

    private static SpriteAssetLayout ParseLayout(string? layout, string name) =>
        layout?.Trim().ToLowerInvariant() switch
        {
            "single" => SpriteAssetLayout.Single,
            "grid" => SpriteAssetLayout.Grid,
            "frames" => SpriteAssetLayout.Frames,
            _ => throw new InvalidDataException($"Sprite '{name}' has unknown layout '{layout}'.")
        };

    private static IReadOnlyList<SpriteAssetFrameDefinition> ParseFrames(
        List<FrameDto?>? source,
        string spriteName)
    {
        if (source is null) return Array.Empty<SpriteAssetFrameDefinition>();
        var result = new SpriteAssetFrameDefinition[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            var frame = source[i]
                ?? throw new InvalidDataException($"Frame {i} of Sprite '{spriteName}' is null.");
            result[i] = new SpriteAssetFrameDefinition(
                frame.Texture,
                frame.Source is null ? null : ParseRect(frame.Source, spriteName));
        }
        return result;
    }

    private static PixelRectI ParseRect(RectDto rect, string spriteName)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidDataException($"Sprite '{spriteName}' has an invalid source rectangle.");
        return new PixelRectI(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static PixelSizeI ParsePixelSize(SizeIntDto size, string spriteName)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidDataException($"Sprite '{spriteName}' frameSize must be positive.");
        return new PixelSizeI(size.Width, size.Height);
    }

    private static PixelSizeI ParsePositiveSize(SizeIntDto size, string fieldName)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidDataException($"{fieldName} must be positive.");
        return new PixelSizeI(size.Width, size.Height);
    }

    private static Vector2 ParseLogicalSize(SizeFloatDto size, string spriteName)
    {
        if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) ||
            size.Width <= 0f || size.Height <= 0f)
            throw new InvalidDataException($"Sprite '{spriteName}' size must be finite and positive.");
        return new Vector2(size.Width, size.Height);
    }

    private static TextureSampler ParseSampler(string? sampling) =>
        sampling?.Trim().ToLowerInvariant() switch
        {
            null or "" or "smooth" => TextureSampler.Smooth,
            "pixelart" or "pixel-art" or "nearest" => TextureSampler.PixelArt,
            _ => throw new InvalidDataException($"Unknown texture sampling preset '{sampling}'.")
        };

    internal sealed class ManifestDto
    {
        public int SchemaVersion { get; init; }
        public string? Id { get; init; }
        public List<DependencyDto?>? Dependencies { get; init; }
        public List<TextureDto?>? Textures { get; init; }
        public List<SpriteDto?>? Sprites { get; init; }
        public List<AnimationDto?>? Animations { get; init; }
        public List<AudioClipDto?>? AudioClips { get; init; }
        public AtlasDto? Atlas { get; init; }
    }

    internal sealed class DependencyDto
    {
        public string? Id { get; init; }
        public string? Manifest { get; init; }
    }

    internal sealed class TextureDto
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
        public string? Sampling { get; init; }
    }

    internal sealed class SpriteDto
    {
        public string? Name { get; init; }
        public string? Texture { get; init; }
        public string? Layout { get; init; }
        public RectDto? Source { get; init; }
        public SizeIntDto? FrameSize { get; init; }
        public int? FrameCount { get; init; }
        public List<FrameDto?>? Frames { get; init; }
        public SizeFloatDto? Size { get; init; }
        public PointDto? Origin { get; init; }
        public float? FramesPerSecond { get; init; }
    }

    internal sealed class FrameDto
    {
        public string? Texture { get; init; }
        public RectDto? Source { get; init; }
    }

    internal sealed class AnimationDto
    {
        public string? Name { get; init; }
        public string? Sprite { get; init; }
        public List<int>? Frames { get; init; }
        public float? FramesPerSecond { get; init; }
        public string? Loop { get; init; }
        public List<AnimationMarkerDto?>? Markers { get; init; }
    }

    internal sealed class AnimationMarkerDto
    {
        public int Frame { get; init; }
        public string? Event { get; init; }
    }

    internal sealed class AudioClipDto
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
        public bool? Streaming { get; init; }
    }

    internal sealed class RectDto
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    internal sealed class SizeIntDto
    {
        public int Width { get; init; }
        public int Height { get; init; }
    }

    internal sealed class SizeFloatDto
    {
        public float Width { get; init; }
        public float Height { get; init; }
    }

    internal sealed class PointDto
    {
        public float? X { get; init; }
        public float? Y { get; init; }
    }

    internal sealed class AtlasDto
    {
        public SizeIntDto? MaxPageSize { get; init; }
        public int? Padding { get; init; }
        public int? Extrude { get; init; }
        public List<string?>? Textures { get; init; }
    }
}
