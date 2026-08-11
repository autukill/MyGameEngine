namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Animation;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.TileWorlds.Domain;
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
        var tileSets = ParseTileSets(document.TileSets);
        var tileMaps = ParseTileMaps(document.TileMaps);
        var tileWorlds = ParseTileWorlds(document.TileWorlds);
        var atlas = ParseAtlas(document.Atlas, textures);
        if (dependencies.Count == 0 && textures.Count == 0 && sprites.Count == 0 && animations.Count == 0 &&
            audioClips.Count == 0 && tileSets.Count == 0 && tileMaps.Count == 0 && tileWorlds.Count == 0)
            throw new InvalidDataException(
                "An asset package must contain at least one dependency, Texture, Sprite, Animation, Audio clip, TileSet, TileMap or TileWorld.");

        return new AssetPackageManifest(
            document.SchemaVersion,
            document.Id,
            dependencies,
            textures,
            sprites,
            animations,
            audioClips,
            atlas,
            tileSets,
            tileMaps,
            tileWorlds);
    }

    private static IReadOnlyList<TileWorldAssetDefinition> ParseTileWorlds(List<TileWorldDto?>? source)
    {
        if (source is null) return Array.Empty<TileWorldAssetDefinition>();
        var result = new TileWorldAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            TileWorldDto item = source[i]
                ?? throw new InvalidDataException($"TileWorld entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"TileWorld entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"TileWorld '{item.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException($"TileWorld '{item.Name}' has no path.");

            TileWorldAssetBuildDefinition? build = null;
            if (item.Build is { } sourceBuild)
            {
                if (sourceBuild.Bounds is null)
                    throw new InvalidDataException($"TileWorld '{item.Name}' build requires bounds.");
                TileWorldBoundsDto bounds = sourceBuild.Bounds;
                TileWorldChunkBounds parsedBounds;
                try
                {
                    parsedBounds = new TileWorldChunkBounds(
                        bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new InvalidDataException($"TileWorld '{item.Name}' has invalid bounds.", exception);
                }
                int lodCount = sourceBuild.LodCount ?? 1;
                if (lodCount is <= 0 or > 8)
                    throw new InvalidDataException($"TileWorld '{item.Name}' lodCount must be in 1..8.");
                PixelSizeI rasterSize = sourceBuild.RasterChunkSize is { } size
                    ? ParsePositiveSize(size, $"TileWorld '{item.Name}' rasterChunkSize")
                    : new PixelSizeI(512, 512);
                AtlasPageEncoding encoding = sourceBuild.Encoding?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "webplossless" => AtlasPageEncoding.WebpLossless,
                    "png" => AtlasPageEncoding.Png,
                    _ => throw new InvalidDataException(
                        $"TileWorld '{item.Name}' has unknown encoding '{sourceBuild.Encoding}'.")
                };
                TextureSampler sampling = ParseSampler(sourceBuild.Sampling);
                int gutter = sourceBuild.Gutter ?? 2;
                if (gutter is < 0 or > 16)
                    throw new InvalidDataException($"TileWorld '{item.Name}' gutter must be in 0..16.");
                IReadOnlyList<TileWorldFallbackSurfaceAssetDefinition> fallbackSurfaces =
                    ParseTileWorldFallbackSurfaces(item.Name, sourceBuild.FallbackSurfaces);
                build = new TileWorldAssetBuildDefinition(
                    parsedBounds, lodCount, rasterSize, encoding, sampling, gutter, fallbackSurfaces);
            }
            else if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(item.Path), ".mgworld"))
            {
                throw new InvalidDataException(
                    $"Authored TileWorld '{item.Name}' requires build settings; compiled paths must use .mgworld.");
            }
            result[i] = new TileWorldAssetDefinition(item.Name, item.Path, build);
        }
        return result;
    }

    private static IReadOnlyList<TileWorldFallbackSurfaceAssetDefinition> ParseTileWorldFallbackSurfaces(
        string worldName,
        List<TileWorldFallbackSurfaceDto?>? source)
    {
        if (source is null) return [];
        var result = new TileWorldFallbackSurfaceAssetDefinition[source.Count];
        var layers = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < source.Count; index++)
        {
            TileWorldFallbackSurfaceDto item = source[index]
                ?? throw new InvalidDataException(
                    $"TileWorld '{worldName}' fallback surface {index} is null.");
            if (string.IsNullOrWhiteSpace(item.Layer))
                throw new InvalidDataException(
                    $"TileWorld '{worldName}' fallback surface {index} has no layer.");
            if (!layers.Add(item.Layer))
                throw new InvalidDataException(
                    $"TileWorld '{worldName}' repeats fallback surface layer '{item.Layer}'.");
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException(
                    $"TileWorld '{worldName}' fallback surface for layer '{item.Layer}' has no path.");
            result[index] = new TileWorldFallbackSurfaceAssetDefinition(
                item.Layer,
                item.Path,
                ParseSampler(item.Sampling));
        }
        return result;
    }

    private static IReadOnlyList<TileSetAssetDefinition> ParseTileSets(List<TileSetDto?>? source)
    {
        if (source is null) return Array.Empty<TileSetAssetDefinition>();
        var result = new TileSetAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            TileSetDto item = source[i]
                ?? throw new InvalidDataException($"TileSet entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"TileSet entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"TileSet '{item.Name}' appears more than once.");
            if (item.TileSize is null || !float.IsFinite(item.TileSize.Width) ||
                !float.IsFinite(item.TileSize.Height) || item.TileSize.Width <= 0f ||
                item.TileSize.Height <= 0f)
                throw new InvalidDataException($"TileSet '{item.Name}' has an invalid tileSize.");
            if (item.Tiles is not { Count: > 0 })
                throw new InvalidDataException($"TileSet '{item.Name}' requires at least one Tile.");

            var tiles = new TileAssetDefinition[item.Tiles.Count];
            var ids = new HashSet<ushort>();
            for (int tileIndex = 0; tileIndex < item.Tiles.Count; tileIndex++)
            {
                TileDto tile = item.Tiles[tileIndex]
                    ?? throw new InvalidDataException($"Tile {tileIndex} of TileSet '{item.Name}' is null.");
                if (tile.Id is <= 0 or > ushort.MaxValue)
                    throw new InvalidDataException(
                        $"Tile {tileIndex} of TileSet '{item.Name}' has an id outside 1..65535.");
                ushort id = (ushort)tile.Id;
                if (!ids.Add(id))
                    throw new InvalidDataException($"TileSet '{item.Name}' repeats Tile id {id}.");
                if (string.IsNullOrWhiteSpace(tile.Sprite))
                    throw new InvalidDataException($"Tile {id} of TileSet '{item.Name}' has no Sprite.");
                int subImage = tile.SubImage ?? 0;
                if (subImage < 0)
                    throw new InvalidDataException($"Tile {id} of TileSet '{item.Name}' has a negative subImage.");
                TileCollisionKind collision = tile.Collision?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "none" => TileCollisionKind.None,
                    "solid" => TileCollisionKind.Solid,
                    _ => throw new InvalidDataException(
                        $"Tile {id} of TileSet '{item.Name}' has unknown collision '{tile.Collision}'.")
                };
                tiles[tileIndex] = new TileAssetDefinition(id, tile.Sprite, subImage, collision);
            }
            result[i] = new TileSetAssetDefinition(
                item.Name,
                new Vector2(item.TileSize.Width, item.TileSize.Height),
                tiles);
        }
        return result;
    }

    private static IReadOnlyList<TileMapAssetDefinition> ParseTileMaps(List<TileMapDto?>? source)
    {
        if (source is null) return Array.Empty<TileMapAssetDefinition>();
        var result = new TileMapAssetDefinition[source.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < source.Count; i++)
        {
            TileMapDto item = source[i]
                ?? throw new InvalidDataException($"TileMap entry {i} is null.");
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"TileMap entry {i} has no name.");
            if (!names.Add(item.Name))
                throw new InvalidDataException($"TileMap '{item.Name}' appears more than once.");
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException($"TileMap '{item.Name}' has no path.");
            result[i] = new TileMapAssetDefinition(item.Name, item.Path);
        }
        return result;
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
        AtlasPageEncoding pageEncoding = source.PageEncoding?.Trim().ToLowerInvariant() switch
        {
            null or "" or "png" => AtlasPageEncoding.Png,
            "webplossless" => AtlasPageEncoding.WebpLossless,
            _ => throw new InvalidDataException(
                $"Atlas pageEncoding '{source.PageEncoding}' is unsupported.")
        };

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

        return new AtlasAssetBuildDefinition(
            maxPageSize,
            padding,
            extrude,
            pageEncoding,
            selected);
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
        public List<TileSetDto?>? TileSets { get; init; }
        public List<TileMapDto?>? TileMaps { get; init; }
        public List<TileWorldDto?>? TileWorlds { get; init; }
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

    internal sealed class TileSetDto
    {
        public string? Name { get; init; }
        public SizeFloatDto? TileSize { get; init; }
        public List<TileDto?>? Tiles { get; init; }
    }

    internal sealed class TileDto
    {
        public int Id { get; init; }
        public string? Sprite { get; init; }
        public int? SubImage { get; init; }
        public string? Collision { get; init; }
    }

    internal sealed class TileMapDto
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
    }

    internal sealed class TileWorldDto
    {
        public string? Name { get; init; }
        public string? Path { get; init; }
        public TileWorldBuildDto? Build { get; init; }
    }

    internal sealed class TileWorldBuildDto
    {
        public TileWorldBoundsDto? Bounds { get; init; }
        public int? LodCount { get; init; }
        public SizeIntDto? RasterChunkSize { get; init; }
        public string? Encoding { get; init; }
        public string? Sampling { get; init; }
        public int? Gutter { get; init; }
        public List<TileWorldFallbackSurfaceDto?>? FallbackSurfaces { get; init; }
    }

    internal sealed class TileWorldFallbackSurfaceDto
    {
        public string? Layer { get; init; }
        public string? Path { get; init; }
        public string? Sampling { get; init; }
    }

    internal sealed class TileWorldBoundsDto
    {
        public int MinX { get; init; }
        public int MinY { get; init; }
        public int MaxX { get; init; }
        public int MaxY { get; init; }
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
        public string? PageEncoding { get; init; }
        public List<string?>? Textures { get; init; }
    }
}
