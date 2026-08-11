namespace GameEngine.Tools.AssetCompiler;

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.Vorbis;
using GameEngine.Features.Animation;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TextureAtlas.Domain;
using GameEngine.Features.TextureAtlas.Infrastructure;
using Imazen.WebP;
using SkiaSharp;

public sealed record ContentAssetCompileResult(
    string PackageId,
    string OutputManifestPath,
    int AtlasPageCount,
    int PackedFrameCount,
    int PassthroughFrameCount);

/// <summary>Compiles authoring manifests into standard runtime packages with offline Atlas pages.</summary>
public sealed class ContentAssetCompiler
{
    private sealed record ManifestContext(
        string Path,
        string Directory,
        AssetPackageManifest Manifest);

    private sealed class SourceTexture(
        TextureAssetDefinition definition,
        string path,
        IImageDecoder decoder)
    {
        private DecodedImage? _decoded;
        public TextureAssetDefinition Definition { get; } = definition;
        public string Path { get; } = path;

        public DecodedImage Decode()
        {
            if (_decoded is { } decoded) return decoded;
            using var stream = File.OpenRead(Path);
            _decoded = decoder.Decode(stream);
            return _decoded.Value;
        }
    }

    private sealed record NormalizedFrame(string TextureName, PixelRectI SourceRect);

    private sealed record NormalizedSprite(
        string Name,
        Vector2 LogicalSize,
        Vector2 Origin,
        float FramesPerSecond,
        IReadOnlyList<NormalizedFrame> Frames);

    private sealed record FrameRemap(string TextureName, PixelRectI SourceRect);

    private readonly IImageDecoder _decoder;
    private readonly TextureAtlasBuilder _atlasBuilder;

    public ContentAssetCompiler(
        IImageDecoder? decoder = null,
        TextureAtlasBuilder? atlasBuilder = null)
    {
        _decoder = decoder ?? new SkiaImageDecoder();
        _atlasBuilder = atlasBuilder ?? new TextureAtlasBuilder();
    }

    public ContentAssetCompileResult Compile(
        string packagesRoot,
        string rootRelativeManifestPath,
        string outputDirectory) =>
        CompileCore(
            packagesRoot,
            rootRelativeManifestPath,
            outputDirectory,
            outputManifestFileName: "assets.json",
            copyDependencies: true);

    internal ContentAssetCompileResult CompilePackageOnly(
        string packagesRoot,
        string rootRelativeManifestPath,
        string outputDirectory,
        string outputManifestFileName) =>
        CompileCore(
            packagesRoot,
            rootRelativeManifestPath,
            outputDirectory,
            outputManifestFileName,
            copyDependencies: false);

    private ContentAssetCompileResult CompileCore(
        string packagesRoot,
        string rootRelativeManifestPath,
        string outputDirectory,
        string outputManifestFileName,
        bool copyDependencies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootRelativeManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputManifestFileName);
        if (Path.GetFileName(outputManifestFileName) != outputManifestFileName)
            throw new ArgumentException("Output manifest name cannot contain a directory.", nameof(outputManifestFileName));

        string root = Path.GetFullPath(packagesRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Packages root '{root}' does not exist.");
        string manifestPath = ResolveUnderRoot(root, rootRelativeManifestPath, "Manifest");
        string output = Path.GetFullPath(outputDirectory);
        EnsureOutputCanBeCreated(output);

        var contexts = new Dictionary<string, ManifestContext>(StringComparer.Ordinal);
        var textures = new Dictionary<string, SourceTexture>(StringComparer.Ordinal);
        ManifestContext package = ReadGraph(root, manifestPath, null, contexts, textures, []);
        if (copyDependencies && contexts.Values.Any(context => context.Manifest.TileWorlds.Count > 0))
        {
            throw new InvalidDataException(
                "TileWorld assets require ContentBuildPipeline so their source manifests can be " +
                "compiled and rewritten atomically. Use the GameEngineAssetCompiler CLI or " +
                "ContentBuildPipeline.Build instead of ContentAssetCompiler.Compile.");
        }
        AtlasAssetBuildDefinition? atlas = package.Manifest.Atlas;
        if (atlas is null)
            throw new InvalidDataException(
                $"Package '{package.Manifest.Id}' has no Atlas build configuration.");

        var normalizedSprites = package.Manifest.Sprites
            .Select(sprite => NormalizeSprite(sprite, textures))
            .ToArray();
        var selectedTextures = atlas.Textures.ToHashSet(StringComparer.Ordinal);
        var referencedSelectedTextures = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSources = new Dictionary<string, AtlasSourceFrame>(StringComparer.Ordinal);
        var keySamplers = new Dictionary<string, TextureSampler>(StringComparer.Ordinal);
        var keyTextureNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sprite in normalizedSprites)
        {
            foreach (var frame in sprite.Frames)
            {
                if (!selectedTextures.Contains(frame.TextureName)) continue;
                referencedSelectedTextures.Add(frame.TextureName);
                string key = FrameKey(frame.TextureName, frame.SourceRect);
                if (uniqueSources.ContainsKey(key)) continue;

                SourceTexture texture = textures[frame.TextureName];
                uniqueSources.Add(key, CropFrame(key, texture.Decode(), frame.SourceRect));
                keySamplers.Add(key, texture.Definition.Sampler);
                keyTextureNames.Add(key, frame.TextureName);
            }
        }

        var remaps = new Dictionary<string, FrameRemap>(StringComparer.Ordinal);
        var generatedTextures = new List<TextureAssetDefinition>();
        var generatedPages = new List<(string RelativePath, AtlasPage Page)>();
        var passthroughKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var samplingGroup in uniqueSources
                     .GroupBy(item => SamplerName(keySamplers[item.Key]), StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            TextureSampler sampler = keySamplers[samplingGroup.First().Key];
            var result = _atlasBuilder.Build(
                samplingGroup.Select(item => item.Value),
                new AtlasBuildOptions(
                    atlas.MaxPageSize.Width,
                    atlas.MaxPageSize.Height,
                    atlas.Padding,
                    atlas.Extrude));

            for (int localPageIndex = 0; localPageIndex < result.Pages.Count; localPageIndex++)
            {
                string textureName = GeneratedTextureName(package.Manifest.Id, samplingGroup.Key, localPageIndex);
                if (textures.ContainsKey(textureName) || generatedTextures.Any(item => item.Name == textureName))
                    throw new InvalidDataException($"Generated Atlas Texture name '{textureName}' conflicts with an asset.");
                string extension = AtlasPageExtension(atlas.PageEncoding);
                string relativePath = $"atlas/{samplingGroup.Key}-{localPageIndex}{extension}";
                generatedTextures.Add(new TextureAssetDefinition(textureName, relativePath, sampler));
                generatedPages.Add((relativePath, result.Pages[localPageIndex]));
            }

            foreach (var placement in result.Placements.Values)
            {
                string textureName = GeneratedTextureName(
                    package.Manifest.Id,
                    samplingGroup.Key,
                    placement.PageIndex);
                remaps.Add(placement.Key, new FrameRemap(textureName, placement.SourceRect));
            }
            passthroughKeys.UnionWith(result.PassthroughKeys);
        }

        var retainedTextures = new List<TextureAssetDefinition>();
        foreach (var definition in package.Manifest.Textures)
        {
            bool selected = selectedTextures.Contains(definition.Name);
            bool referenced = referencedSelectedTextures.Contains(definition.Name);
            bool hasPassthrough = passthroughKeys.Any(key =>
                StringComparer.Ordinal.Equals(keyTextureNames[key], definition.Name));
            if (!selected || !referenced || hasPassthrough)
                retainedTextures.Add(definition);
        }
        retainedTextures.AddRange(generatedTextures);

        string temporary = output + $".tmp-{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporary);
        try
        {
            ValidateOutputPaths(
                temporary,
                retainedTextures,
                package.Manifest.AudioClips,
                package.Manifest.TileMaps,
                package.Manifest.TileWorlds);
            CopyRetainedTextures(package, retainedTextures, generatedTextures, temporary);
            CopyAudioClips(package, temporary);
            CopyTileMaps(package, temporary);
            if (copyDependencies)
                CopyDependencyPackages(root, contexts.Values, package, temporary);
            foreach (var generated in generatedPages)
            {
                string pagePath = ResolveOutputPath(temporary, generated.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
                WriteAtlasPage(pagePath, generated.Page, atlas.PageEncoding);
            }

            string outputManifest = Path.Combine(temporary, outputManifestFileName);
            WriteCompiledManifest(
                outputManifest,
                package.Manifest,
                retainedTextures,
                normalizedSprites,
                remaps);

            if (Directory.Exists(output))
                Directory.Delete(output, recursive: false);
            Directory.Move(temporary, output);

            return new ContentAssetCompileResult(
                package.Manifest.Id,
                Path.Combine(output, outputManifestFileName),
                generatedPages.Count,
                remaps.Count,
                passthroughKeys.Count);
        }
        catch
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    private ManifestContext ReadGraph(
        string packagesRoot,
        string manifestPath,
        string? expectedId,
        Dictionary<string, ManifestContext> contexts,
        Dictionary<string, SourceTexture> textures,
        HashSet<string> visiting)
    {
        if (visiting.Contains(manifestPath))
            throw new InvalidDataException($"Content package dependency cycle reaches '{manifestPath}'.");
        if (contexts.Values.FirstOrDefault(item => PathEquals(item.Path, manifestPath)) is { } known)
        {
            if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, known.Manifest.Id))
                throw new InvalidDataException($"Dependency expected '{expectedId}', but found '{known.Manifest.Id}'.");
            return known;
        }

        using var stream = File.OpenRead(manifestPath);
        var manifest = AssetPackageManifestParser.Parse(stream);
        if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, manifest.Id))
            throw new InvalidDataException($"Dependency expected '{expectedId}', but found '{manifest.Id}'.");
        if (contexts.TryGetValue(manifest.Id, out var sameId) && !PathEquals(sameId.Path, manifestPath))
            throw new InvalidDataException($"Package id '{manifest.Id}' resolves to multiple manifests.");

        string directory = Path.GetDirectoryName(manifestPath)!;
        var context = new ManifestContext(manifestPath, directory, manifest);
        contexts.Add(manifest.Id, context);
        visiting.Add(manifestPath);
        try
        {
            foreach (var definition in manifest.Textures)
            {
                string path = ResolveUnderRoot(directory, definition.Path, "Texture");
                if (!textures.TryAdd(definition.Name, new SourceTexture(definition, path, _decoder)))
                    throw new InvalidDataException($"Texture name '{definition.Name}' appears in multiple packages.");
            }
            foreach (var dependency in manifest.Dependencies)
            {
                string dependencyPath = ResolveUnderRoot(packagesRoot, dependency.Manifest, "Dependency manifest");
                ReadGraph(packagesRoot, dependencyPath, dependency.Id, contexts, textures, visiting);
            }
        }
        finally
        {
            visiting.Remove(manifestPath);
        }
        return context;
    }

    private static NormalizedSprite NormalizeSprite(
        SpriteAssetDefinition sprite,
        IReadOnlyDictionary<string, SourceTexture> textures)
    {
        NormalizedFrame[] frames = sprite.Layout switch
        {
            SpriteAssetLayout.Single =>
            [NormalizeFrame(sprite.TextureName!, sprite.SourceRect, textures)],
            SpriteAssetLayout.Grid => NormalizeGrid(sprite, textures),
            SpriteAssetLayout.Frames => sprite.Frames
                .Select(frame => NormalizeFrame(
                    frame.TextureName ?? sprite.TextureName!,
                    frame.SourceRect,
                    textures))
                .ToArray(),
            _ => throw new InvalidDataException($"Unsupported Sprite layout '{sprite.Layout}'.")
        };
        int frameWidth = frames[0].SourceRect.Width;
        int frameHeight = frames[0].SourceRect.Height;
        if (frames.Any(frame =>
                frame.SourceRect.Width != frameWidth || frame.SourceRect.Height != frameHeight))
        {
            throw new InvalidDataException(
                $"Sprite '{sprite.Name}' frames must have identical source dimensions.");
        }

        var logicalSize = sprite.LogicalSize ??
            new Vector2(frames[0].SourceRect.Width, frames[0].SourceRect.Height);
        return new NormalizedSprite(
            sprite.Name,
            logicalSize,
            sprite.Origin,
            sprite.FramesPerSecond,
            frames);
    }

    private static NormalizedFrame[] NormalizeGrid(
        SpriteAssetDefinition sprite,
        IReadOnlyDictionary<string, SourceTexture> textures)
    {
        string textureName = sprite.TextureName!;
        SourceTexture texture = GetTexture(textureName, textures);
        DecodedImage decoded = texture.Decode();
        PixelSizeI size = sprite.FrameSize!.Value;
        int count = sprite.FrameCount!.Value;
        int columns = decoded.Width / size.Width;
        int rows = decoded.Height / size.Height;
        if (columns <= 0 || rows <= 0 || count > checked(columns * rows))
            throw new InvalidDataException($"Grid Sprite '{sprite.Name}' exceeds Texture '{textureName}'.");

        var frames = new NormalizedFrame[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = new NormalizedFrame(
                textureName,
                new PixelRectI(
                    (i % columns) * size.Width,
                    (i / columns) * size.Height,
                    size.Width,
                    size.Height));
        }
        return frames;
    }

    private static NormalizedFrame NormalizeFrame(
        string textureName,
        PixelRectI? sourceRect,
        IReadOnlyDictionary<string, SourceTexture> textures)
    {
        SourceTexture texture = GetTexture(textureName, textures);
        DecodedImage decoded = texture.Decode();
        PixelRectI rect = sourceRect ?? new PixelRectI(0, 0, decoded.Width, decoded.Height);
        ValidateRect(textureName, rect, decoded);
        return new NormalizedFrame(textureName, rect);
    }

    private static SourceTexture GetTexture(
        string name,
        IReadOnlyDictionary<string, SourceTexture> textures) =>
        textures.TryGetValue(name, out var texture)
            ? texture
            : throw new InvalidDataException($"Texture '{name}' is unavailable to the compiler.");

    private static AtlasSourceFrame CropFrame(string key, DecodedImage image, PixelRectI rect)
    {
        ValidateRect(key, rect, image);
        var pixels = new byte[checked(rect.Width * rect.Height * 4)];
        int sourceStride = image.Width * 4;
        int targetStride = rect.Width * 4;
        for (int row = 0; row < rect.Height; row++)
        {
            Buffer.BlockCopy(
                image.RgbaPixels,
                (rect.Y + row) * sourceStride + rect.X * 4,
                pixels,
                row * targetStride,
                targetStride);
        }
        return new AtlasSourceFrame(key, rect.Width, rect.Height, pixels);
    }

    private static void ValidateRect(string name, PixelRectI rect, DecodedImage image)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.Right > image.Width || rect.Bottom > image.Height)
        {
            throw new InvalidDataException($"Frame rectangle for '{name}' exceeds its source Texture.");
        }
    }

    private static void CopyRetainedTextures(
        ManifestContext package,
        IReadOnlyList<TextureAssetDefinition> retained,
        IReadOnlyList<TextureAssetDefinition> generated,
        string output)
    {
        var generatedNames = generated.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in retained)
        {
            if (generatedNames.Contains(definition.Name)) continue;
            string source = ResolveUnderRoot(package.Directory, definition.Path, "Texture");
            string destination = ResolveOutputPath(output, definition.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void ValidateOutputPaths(
        string output,
        IEnumerable<TextureAssetDefinition> textures,
        IEnumerable<AudioAssetDefinition> audioClips,
        IEnumerable<TileMapAssetDefinition> tileMaps,
        IEnumerable<TileWorldAssetDefinition> tileWorlds)
    {
        var paths = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var texture in textures)
        {
            string path = ResolveOutputPath(output, texture.Path);
            if (!paths.Add(path))
            {
                throw new InvalidDataException(
                    $"Texture output path '{texture.Path}' is used more than once.");
            }
        }
        foreach (AudioAssetDefinition audio in audioClips)
        {
            string path = ResolveOutputPath(output, audio.Path);
            if (!paths.Add(path))
                throw new InvalidDataException($"Asset output path '{audio.Path}' is used more than once.");
        }
        foreach (TileMapAssetDefinition tileMap in tileMaps)
        {
            string path = ResolveOutputPath(output, tileMap.Path);
            if (!paths.Add(path))
                throw new InvalidDataException($"Asset output path '{tileMap.Path}' is used more than once.");
        }
        foreach (TileWorldAssetDefinition tileWorld in tileWorlds)
        {
            string relative = CompiledTileWorldPath(tileWorld.Path);
            string path = ResolveOutputPath(output, relative);
            if (!paths.Add(path))
                throw new InvalidDataException($"Asset output path '{relative}' is used more than once.");
        }
    }

    private static void CopyAudioClips(ManifestContext package, string output)
    {
        foreach (AudioAssetDefinition definition in package.Manifest.AudioClips)
        {
            string source = ResolveUnderRoot(package.Directory, definition.Path, "Audio clip");
            ValidateAudioFile(definition, source);
            string destination = ResolveOutputPath(output, definition.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void ValidateAudioFile(AudioAssetDefinition definition, string path)
    {
        string expectedExtension = definition.Streaming ? ".ogg" : ".wav";
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(path), expectedExtension))
            throw new InvalidDataException(
                $"Audio clip '{definition.Name}' must use a {expectedExtension} asset when streaming is {definition.Streaming.ToString().ToLowerInvariant()}.");
        if (definition.Streaming)
            _ = VorbisAudioStreamFactory.ReadMetadata(path);
        else
            _ = WaveAudioDecoder.DecodeFile(path);
    }

    private static void CopyTileMaps(ManifestContext package, string output)
    {
        foreach (TileMapAssetDefinition definition in package.Manifest.TileMaps)
        {
            string source = ResolveUnderRoot(package.Directory, definition.Path, "TileMap");
            string destination = ResolveOutputPath(output, definition.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
    }

    private static void CopyDependencyPackages(
        string packagesRoot,
        IEnumerable<ManifestContext> contexts,
        ManifestContext rootPackage,
        string output)
    {
        foreach (var context in contexts)
        {
            if (PathEquals(context.Path, rootPackage.Path)) continue;

            string relativeManifest = Path.GetRelativePath(packagesRoot, context.Path);
            string targetManifest = ResolveOutputPath(output, relativeManifest);
            Directory.CreateDirectory(Path.GetDirectoryName(targetManifest)!);
            File.Copy(context.Path, targetManifest, overwrite: false);

            string relativePackageDirectory = Path.GetDirectoryName(relativeManifest) ?? string.Empty;
            foreach (var texture in context.Manifest.Textures)
            {
                string source = ResolveUnderRoot(context.Directory, texture.Path, "Dependency Texture");
                string relativeTarget = Path.Combine(relativePackageDirectory, texture.Path);
                string target = ResolveOutputPath(output, relativeTarget);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                    File.Copy(source, target, overwrite: false);
            }
            foreach (AudioAssetDefinition audio in context.Manifest.AudioClips)
            {
                string source = ResolveUnderRoot(context.Directory, audio.Path, "Dependency Audio clip");
                string relativeTarget = Path.Combine(relativePackageDirectory, audio.Path);
                string target = ResolveOutputPath(output, relativeTarget);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                    File.Copy(source, target, overwrite: false);
            }
            foreach (TileMapAssetDefinition tileMap in context.Manifest.TileMaps)
            {
                string source = ResolveUnderRoot(context.Directory, tileMap.Path, "Dependency TileMap");
                string relativeTarget = Path.Combine(relativePackageDirectory, tileMap.Path);
                string target = ResolveOutputPath(output, relativeTarget);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                    File.Copy(source, target, overwrite: false);
            }
        }
    }

    private static void WriteCompiledManifest(
        string path,
        AssetPackageManifest source,
        IReadOnlyList<TextureAssetDefinition> textures,
        IReadOnlyList<NormalizedSprite> sprites,
        IReadOnlyDictionary<string, FrameRemap> remaps)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", source.SchemaVersion);
        writer.WriteString("id", source.Id);
        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in source.Dependencies)
        {
            writer.WriteStartObject();
            writer.WriteString("id", dependency.Id);
            writer.WriteString("manifest", dependency.Manifest);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("textures");
        writer.WriteStartArray();
        foreach (var texture in textures)
        {
            writer.WriteStartObject();
            writer.WriteString("name", texture.Name);
            writer.WriteString("path", texture.Path.Replace('\\', '/'));
            writer.WriteString("sampling", SamplerName(texture.Sampler) == "pixel-art" ? "pixelArt" : "smooth");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("sprites");
        writer.WriteStartArray();
        foreach (var sprite in sprites)
        {
            writer.WriteStartObject();
            writer.WriteString("name", sprite.Name);
            writer.WriteString("layout", "frames");
            writer.WritePropertyName("frames");
            writer.WriteStartArray();
            foreach (var frame in sprite.Frames)
            {
                string key = FrameKey(frame.TextureName, frame.SourceRect);
                FrameRemap resolved = remaps.TryGetValue(key, out var remap)
                    ? remap
                    : new FrameRemap(frame.TextureName, frame.SourceRect);
                writer.WriteStartObject();
                writer.WriteString("texture", resolved.TextureName);
                WriteRect(writer, resolved.SourceRect);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            WriteSize(writer, "size", sprite.LogicalSize.X, sprite.LogicalSize.Y);
            WritePoint(writer, "origin", sprite.Origin.X, sprite.Origin.Y);
            writer.WriteNumber("framesPerSecond", sprite.FramesPerSecond);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("animations");
        writer.WriteStartArray();
        foreach (AnimationAssetDefinition animation in source.Animations)
        {
            writer.WriteStartObject();
            writer.WriteString("name", animation.Name);
            writer.WriteString("sprite", animation.SpriteName);
            writer.WritePropertyName("frames");
            writer.WriteStartArray();
            foreach (int frame in animation.Frames)
                writer.WriteNumberValue(frame);
            writer.WriteEndArray();
            writer.WriteNumber("framesPerSecond", animation.FramesPerSecond);
            writer.WriteString("loop", animation.LoopMode switch
            {
                AnimationLoopMode.Once => "once",
                AnimationLoopMode.Loop => "loop",
                AnimationLoopMode.PingPong => "pingPong",
                _ => throw new InvalidDataException(
                    $"Animation '{animation.Name}' has an unsupported loop mode.")
            });
            writer.WritePropertyName("markers");
            writer.WriteStartArray();
            foreach (AnimationAssetMarkerDefinition marker in animation.Markers)
            {
                writer.WriteStartObject();
                writer.WriteNumber("frame", marker.Frame);
                writer.WriteString("event", marker.Event);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("audioClips");
        writer.WriteStartArray();
        foreach (AudioAssetDefinition audio in source.AudioClips)
        {
            writer.WriteStartObject();
            writer.WriteString("name", audio.Name);
            writer.WriteString("path", audio.Path.Replace('\\', '/'));
            writer.WriteBoolean("streaming", audio.Streaming);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("tileSets");
        writer.WriteStartArray();
        foreach (TileSetAssetDefinition tileSet in source.TileSets)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tileSet.Name);
            WriteSize(writer, "tileSize", tileSet.TileSize.X, tileSet.TileSize.Y);
            writer.WritePropertyName("tiles");
            writer.WriteStartArray();
            foreach (TileAssetDefinition tile in tileSet.Tiles)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", tile.Id);
                writer.WriteString("sprite", tile.SpriteName);
                writer.WriteNumber("subImage", tile.SubImage);
                writer.WriteString("collision", tile.Collision == GameEngine.Features.Tilemaps.Domain.TileCollisionKind.Solid
                    ? "solid"
                    : "none");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("tileMaps");
        writer.WriteStartArray();
        foreach (TileMapAssetDefinition tileMap in source.TileMaps)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tileMap.Name);
            writer.WriteString("path", tileMap.Path.Replace('\\', '/'));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WritePropertyName("tileWorlds");
        writer.WriteStartArray();
        foreach (TileWorldAssetDefinition tileWorld in source.TileWorlds)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tileWorld.Name);
            writer.WriteString("path", tileWorld.Path.Replace('\\', '/'));
            if (tileWorld.Build is { } build)
            {
                writer.WritePropertyName("build");
                writer.WriteStartObject();
                writer.WritePropertyName("bounds");
                writer.WriteStartObject();
                writer.WriteNumber("minX", build.Bounds.MinX);
                writer.WriteNumber("minY", build.Bounds.MinY);
                writer.WriteNumber("maxX", build.Bounds.MaxX);
                writer.WriteNumber("maxY", build.Bounds.MaxY);
                writer.WriteEndObject();
                writer.WriteNumber("lodCount", build.LodCount);
                WriteSize(writer, "rasterChunkSize", build.RasterChunkSize.Width, build.RasterChunkSize.Height);
                writer.WriteString("encoding", build.Encoding == AtlasPageEncoding.Png ? "png" : "webpLossless");
                writer.WriteString("sampling", build.Sampling == TextureSampler.PixelArt ? "pixelArt" : "smooth");
                writer.WriteNumber("gutter", build.Gutter);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static string CompiledTileWorldPath(string sourcePath)
    {
        string normalized = sourcePath.Replace('\\', '/');
        const string preTiledSuffix = ".pretiledworld.json";
        if (normalized.EndsWith(preTiledSuffix, StringComparison.OrdinalIgnoreCase))
            return normalized[..^preTiledSuffix.Length] + ".mgworld";
        const string tileMapSuffix = ".tilemap.json";
        return normalized.EndsWith(tileMapSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^tileMapSuffix.Length] + ".mgworld"
            : Path.ChangeExtension(normalized, ".mgworld").Replace('\\', '/');
    }

    private static void WriteRect(Utf8JsonWriter writer, PixelRectI rect)
    {
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteNumber("x", rect.X);
        writer.WriteNumber("y", rect.Y);
        writer.WriteNumber("width", rect.Width);
        writer.WriteNumber("height", rect.Height);
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, string name, float width, float height)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("width", width);
        writer.WriteNumber("height", height);
        writer.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter writer, string name, float x, float y)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("x", x);
        writer.WriteNumber("y", y);
        writer.WriteEndObject();
    }

    private static void WriteAtlasPage(
        string path,
        AtlasPage page,
        AtlasPageEncoding encoding)
    {
        using var stream = File.Create(path);
        switch (encoding)
        {
            case AtlasPageEncoding.Png:
                WritePng(stream, page, path);
                break;
            case AtlasPageEncoding.WebpLossless:
                var config = new WebPEncoderConfig()
                    .SetLosslessPreset(9)
                    .SetExact();
                WebPEncoder.Encode(
                    page.RgbaPixels,
                    page.Width,
                    page.Height,
                    checked(page.Width * 4),
                    WebPPixelFormat.Rgba,
                    config,
                    stream);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported Atlas page encoding '{encoding}'.");
        }
    }

    private static void WritePng(Stream stream, AtlasPage page, string path)
    {
        var info = new SKImageInfo(
            page.Width,
            page.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(page.RgbaPixels, 0, bitmap.GetPixels(), page.RgbaPixels.Length);
        using SKPixmap pixmap = bitmap.PeekPixels();
        if (!pixmap.Encode(
                stream,
                new SKPngEncoderOptions(
                    SKPngEncoderFilterFlags.AllFilters,
                    6)))
        {
            throw new InvalidOperationException($"Could not encode Atlas page '{path}'.");
        }
    }

    private static string AtlasPageExtension(AtlasPageEncoding encoding) => encoding switch
    {
        AtlasPageEncoding.Png => ".png",
        AtlasPageEncoding.WebpLossless => ".webp",
        _ => throw new InvalidOperationException($"Unsupported Atlas page encoding '{encoding}'.")
    };

    private static string FrameKey(string textureName, PixelRectI rect) =>
        $"{textureName.Length}:{textureName}|{rect.X},{rect.Y},{rect.Width},{rect.Height}";

    private static string GeneratedTextureName(string packageId, string group, int page) =>
        $"__atlas.{packageId}.{group}.{page}";

    private static string SamplerName(TextureSampler sampler) =>
        sampler == TextureSampler.PixelArt ? "pixel-art" : "smooth";

    private static void EnsureOutputCanBeCreated(string output)
    {
        if (File.Exists(output))
            throw new IOException($"Atlas compiler output '{output}' is a file.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"Atlas compiler output '{output}' must be empty or absent.");
    }

    private static string ResolveUnderRoot(string root, string relativePath, string kind)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{kind} paths must be non-empty and relative.");
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, full);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"{kind} path '{relativePath}' escapes its configured root.");
        }
        return full;
    }

    private static string ResolveOutputPath(string output, string relativePath) =>
        ResolveUnderRoot(output, relativePath, "Output");

    private static bool PathEquals(string left, string right) =>
        (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Equals(left, right);
}
