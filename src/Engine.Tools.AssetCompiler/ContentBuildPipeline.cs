namespace GameEngine.Tools.AssetCompiler;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.Vorbis;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>
/// Builds a complete content dependency graph into an owned, fingerprinted runtime packages root.
/// </summary>
public sealed class ContentBuildPipeline
{
    public const string CompilerVersion = "9";
    public const string MetadataFileName = ".mygame-assets.json";
    private const string OwnerName = "MyGameEngine.AssetCompiler";
    private const int MetadataSchemaVersion = 1;

    private sealed class GraphNode
    {
        public required string ManifestPath { get; init; }
        public required string RelativeManifestPath { get; init; }
        public required string PackageDirectory { get; init; }
        public required AssetPackageManifest Manifest { get; init; }
        public List<GraphNode> Dependencies { get; } = [];
    }

    private sealed class BuildMetadata
    {
        public int SchemaVersion { get; init; }
        public string Owner { get; init; } = string.Empty;
        public string CompilerVersion { get; init; } = string.Empty;
        public string RootPackageId { get; init; } = string.Empty;
        public string RootManifest { get; init; } = string.Empty;
        public string InputFingerprint { get; init; } = string.Empty;
        public int PackageCount { get; init; }
        public int AtlasPageCount { get; init; }
        public int PackedFrameCount { get; init; }
        public int PassthroughFrameCount { get; init; }
        public int TileWorldCount { get; init; }
        public int TileWorldChunkCount { get; init; }
        public int TileWorldRasterChunkCount { get; init; }
        public List<OutputFileHash> Outputs { get; init; } = [];
        public List<PackageBuildMetadata> Packages { get; init; } = [];
    }

    private sealed record OutputFileHash(string Path, string Sha256);

    private sealed class PackageBuildMetadata
    {
        public string Id { get; init; } = string.Empty;
        public string Manifest { get; init; } = string.Empty;
        public string InputFingerprint { get; init; } = string.Empty;
        public int AtlasPageCount { get; init; }
        public int PackedFrameCount { get; init; }
        public int PassthroughFrameCount { get; init; }
        public int TileWorldCount { get; init; }
        public int TileWorldChunkCount { get; init; }
        public int TileWorldRasterChunkCount { get; init; }
        public List<OutputFileHash> Outputs { get; init; } = [];
    }

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ContentAssetCompiler _packageCompiler;
    private readonly IImageDecoder _imageDecoder;

    public ContentBuildPipeline(
        ContentAssetCompiler? packageCompiler = null,
        IImageDecoder? imageDecoder = null)
    {
        _packageCompiler = packageCompiler ?? new ContentAssetCompiler();
        _imageDecoder = imageDecoder ?? new SkiaImageDecoder();
    }

    public ContentBuildResult Build(ContentBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackagesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootRelativeManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        string packagesRoot = Path.GetFullPath(request.PackagesRoot);
        if (!Directory.Exists(packagesRoot))
            throw new DirectoryNotFoundException($"Packages root '{packagesRoot}' does not exist.");
        string output = Path.GetFullPath(request.OutputDirectory);
        ValidateOutputBoundary(packagesRoot, output);

        string rootManifest = ResolveUnderRoot(
            packagesRoot,
            request.RootRelativeManifestPath,
            "Root manifest");
        var nodesByPath = new Dictionary<string, GraphNode>(PathComparer);
        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        GraphNode root = ReadGraph(
            packagesRoot,
            rootManifest,
            null,
            nodesByPath,
            nodesById,
            []);
        GraphNode[] graph = nodesById.Values
            .OrderBy(node => PackageDepth(node.RelativeManifestPath))
            .ThenBy(node => node.RelativeManifestPath, StringComparer.Ordinal)
            .ToArray();
        ValidateGraph(graph);

        IReadOnlyDictionary<string, string> packageFingerprints =
            ComputePackageFingerprints(packagesRoot, graph);
        string fingerprint = packageFingerprints[root.Manifest.Id];
        BuildMetadata? current = TryReadMetadata(output);
        bool upToDate = IsUpToDate(
            output,
            current,
            root.Manifest.Id,
            NormalizeRelativePath(request.RootRelativeManifestPath),
            fingerprint);

        if (request.Mode == ContentBuildMode.Check)
        {
            return ResultFromMetadata(
                root,
                output,
                request.RootRelativeManifestPath,
                fingerprint,
                upToDate ? ContentBuildStatus.UpToDate : ContentBuildStatus.Stale,
                current,
                graph.Length);
        }
        if (request.Mode == ContentBuildMode.Incremental && upToDate)
        {
            return ResultFromMetadata(
                root,
                output,
                request.RootRelativeManifestPath,
                fingerprint,
                ContentBuildStatus.UpToDate,
                current,
                graph.Length);
        }

        EnsureReplaceableOutput(output, current);
        string staging = output + $".tmp-{Guid.NewGuid():N}";
        string backup = output + $".backup-{Guid.NewGuid():N}";
        int atlasPages = 0;
        int packedFrames = 0;
        int passthroughFrames = 0;
        int tileWorlds = 0;
        int tileWorldChunks = 0;
        int tileWorldRasterChunks = 0;
        int builtPackages = 0;
        int reusedPackages = 0;
        var packageMetadata = new List<PackageBuildMetadata>(graph.Length);

        try
        {
            foreach (var node in graph)
            {
                string packageFingerprint = packageFingerprints[node.Manifest.Id];
                PackageBuildMetadata? cachedPackage = current?.Packages.FirstOrDefault(item =>
                    StringComparer.Ordinal.Equals(item.Id, node.Manifest.Id) &&
                    StringComparer.Ordinal.Equals(item.InputFingerprint, packageFingerprint));
                if (request.Mode == ContentBuildMode.Incremental &&
                    cachedPackage is not null &&
                    ArePackageOutputsValid(output, cachedPackage))
                {
                    CopyCachedPackageOutputs(output, staging, cachedPackage);
                    packageMetadata.Add(cachedPackage);
                    atlasPages += cachedPackage.AtlasPageCount;
                    packedFrames += cachedPackage.PackedFrameCount;
                    passthroughFrames += cachedPackage.PassthroughFrameCount;
                    tileWorlds += cachedPackage.TileWorldCount;
                    tileWorldChunks += cachedPackage.TileWorldChunkCount;
                    tileWorldRasterChunks += cachedPackage.TileWorldRasterChunkCount;
                    reusedPackages++;
                    continue;
                }

                int packageAtlasPages = 0;
                int packagePackedFrames = 0;
                int packagePassthroughFrames = 0;
                int packageTileWorlds = 0;
                int packageTileWorldChunks = 0;
                int packageTileWorldRasterChunks = 0;
                if (node.Manifest.Atlas is null)
                {
                    CopyRawPackage(staging, node);
                }

                else
                {
                    string isolatedPackage = output + $".package-{Guid.NewGuid():N}";
                    try
                    {
                        string relativePackageDirectory =
                            Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
                        var compiled = _packageCompiler.CompilePackageOnly(
                            packagesRoot,
                            node.RelativeManifestPath,
                            isolatedPackage,
                            Path.GetFileName(node.RelativeManifestPath));
                        CopyDirectory(
                            isolatedPackage,
                            ResolveOutputPath(staging, relativePackageDirectory));
                        packageAtlasPages = compiled.AtlasPageCount;
                        packagePackedFrames = compiled.PackedFrameCount;
                        packagePassthroughFrames = compiled.PassthroughFrameCount;
                    }
                    finally
                    {
                        DeleteDirectoryIfExists(isolatedPackage);
                    }
                }

                (packageTileWorlds, packageTileWorldChunks, packageTileWorldRasterChunks) =
                    CompileTileWorlds(staging, node);

                atlasPages += packageAtlasPages;
                packedFrames += packagePackedFrames;
                passthroughFrames += packagePassthroughFrames;
                tileWorlds += packageTileWorlds;
                tileWorldChunks += packageTileWorldChunks;
                tileWorldRasterChunks += packageTileWorldRasterChunks;
                packageMetadata.Add(new PackageBuildMetadata
                {
                    Id = node.Manifest.Id,
                    Manifest = node.RelativeManifestPath,
                    InputFingerprint = packageFingerprint,
                    AtlasPageCount = packageAtlasPages,
                    PackedFrameCount = packagePackedFrames,
                    PassthroughFrameCount = packagePassthroughFrames,
                    TileWorldCount = packageTileWorlds,
                    TileWorldChunkCount = packageTileWorldChunks,
                    TileWorldRasterChunkCount = packageTileWorldRasterChunks,
                    Outputs = HashPackageOutput(staging, node)
                });
                builtPackages++;
            }

            string outputManifest = ResolveOutputPath(
                staging,
                NormalizeRelativePath(request.RootRelativeManifestPath));
            if (!File.Exists(outputManifest))
                throw new InvalidDataException("The build did not produce the root runtime manifest.");

            var metadata = new BuildMetadata
            {
                SchemaVersion = MetadataSchemaVersion,
                Owner = OwnerName,
                CompilerVersion = CompilerVersion,
                RootPackageId = root.Manifest.Id,
                RootManifest = NormalizeRelativePath(request.RootRelativeManifestPath),
                InputFingerprint = fingerprint,
                PackageCount = graph.Length,
                AtlasPageCount = atlasPages,
                PackedFrameCount = packedFrames,
                PassthroughFrameCount = passthroughFrames,
                TileWorldCount = tileWorlds,
                TileWorldChunkCount = tileWorldChunks,
                TileWorldRasterChunkCount = tileWorldRasterChunks,
                Outputs = HashOutputFiles(staging),
                Packages = packageMetadata
                    .OrderBy(item => item.Manifest, StringComparer.Ordinal)
                    .ToList()
            };
            WriteMetadata(staging, metadata);
            ReplaceOutputAtomically(output, staging, backup);

            return new ContentBuildResult(
                root.Manifest.Id,
                ResolveOutputPath(output, NormalizeRelativePath(request.RootRelativeManifestPath)),
                fingerprint,
                ContentBuildStatus.Built,
                graph.Length,
                builtPackages,
                reusedPackages,
                atlasPages,
                packedFrames,
                passthroughFrames,
                tileWorlds,
                tileWorldChunks,
                tileWorldRasterChunks);
        }
        catch
        {
            DeleteDirectoryIfExists(staging);
            if (Directory.Exists(backup) && !Directory.Exists(output))
                Directory.Move(backup, output);
            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(backup);
        }
    }

    private static ContentBuildResult ResultFromMetadata(
        GraphNode root,
        string output,
        string rootRelativeManifestPath,
        string fingerprint,
        ContentBuildStatus status,
        BuildMetadata? metadata,
        int packageCount) => new(
            root.Manifest.Id,
            ResolveOutputPath(output, NormalizeRelativePath(rootRelativeManifestPath)),
            fingerprint,
            status,
            metadata?.PackageCount ?? packageCount,
            0,
            status == ContentBuildStatus.UpToDate ? packageCount : 0,
            metadata?.AtlasPageCount ?? 0,
            metadata?.PackedFrameCount ?? 0,
            metadata?.PassthroughFrameCount ?? 0,
            metadata?.TileWorldCount ?? 0,
            metadata?.TileWorldChunkCount ?? 0,
            metadata?.TileWorldRasterChunkCount ?? 0);

    private static GraphNode ReadGraph(
        string packagesRoot,
        string manifestPath,
        string? expectedId,
        Dictionary<string, GraphNode> nodesByPath,
        Dictionary<string, GraphNode> nodesById,
        HashSet<string> visiting)
    {
        if (visiting.Contains(manifestPath))
            throw new InvalidDataException($"Content package dependency cycle reaches '{manifestPath}'.");
        if (nodesByPath.TryGetValue(manifestPath, out var known))
        {
            if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, known.Manifest.Id))
                throw new InvalidDataException($"Dependency expected '{expectedId}', but found '{known.Manifest.Id}'.");
            return known;
        }

        using var stream = File.OpenRead(manifestPath);
        AssetPackageManifest manifest = AssetPackageManifestParser.Parse(stream);
        if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, manifest.Id))
            throw new InvalidDataException($"Dependency expected '{expectedId}', but found '{manifest.Id}'.");
        if (nodesById.TryGetValue(manifest.Id, out var sameId) &&
            !PathComparer.Equals(sameId.ManifestPath, manifestPath))
        {
            throw new InvalidDataException($"Package id '{manifest.Id}' resolves to multiple manifests.");
        }

        string relativeManifest = NormalizeRelativePath(Path.GetRelativePath(packagesRoot, manifestPath));
        var node = new GraphNode
        {
            ManifestPath = manifestPath,
            RelativeManifestPath = relativeManifest,
            PackageDirectory = Path.GetDirectoryName(manifestPath)!,
            Manifest = manifest
        };
        nodesByPath.Add(manifestPath, node);
        nodesById.Add(manifest.Id, node);
        visiting.Add(manifestPath);
        try
        {
            foreach (var dependency in manifest.Dependencies)
            {
                string dependencyPath = ResolveUnderRoot(
                    packagesRoot,
                    dependency.Manifest,
                    "Dependency manifest");
                node.Dependencies.Add(ReadGraph(
                    packagesRoot,
                    dependencyPath,
                    dependency.Id,
                    nodesByPath,
                    nodesById,
                    visiting));
            }
        }
        finally
        {
            visiting.Remove(manifestPath);
        }
        return node;
    }

    private void ValidateGraph(IReadOnlyList<GraphNode> graph)
    {
        var packageDirectories = new HashSet<string>(PathComparer);
        var textureOwners = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var spriteNames = new HashSet<string>(StringComparer.Ordinal);
        var animationNames = new HashSet<string>(StringComparer.Ordinal);
        var audioNames = new HashSet<string>(StringComparer.Ordinal);
        var tileSetNames = new HashSet<string>(StringComparer.Ordinal);
        var tileMapNames = new HashSet<string>(StringComparer.Ordinal);
        var tileWorldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph)
        {
            if (!packageDirectories.Add(node.PackageDirectory))
            {
                throw new InvalidDataException(
                    $"Multiple package manifests share directory '{node.PackageDirectory}'.");
            }
            foreach (var texture in node.Manifest.Textures)
            {
                if (!textureOwners.TryAdd(texture.Name, node))
                    throw new InvalidDataException($"Texture '{texture.Name}' appears in multiple packages.");
                string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
                string normalizedTexturePath = Path.GetRelativePath(
                    node.PackageDirectory,
                    Path.GetFullPath(Path.Combine(node.PackageDirectory, texture.Path)));
                string outputPath = NormalizeRelativePath(
                    Path.Combine(relativeDirectory, normalizedTexturePath));
                if (StringComparer.OrdinalIgnoreCase.Equals(outputPath, MetadataFileName))
                    throw new InvalidDataException($"Texture '{texture.Name}' uses reserved build metadata path.");
            }
            foreach (var sprite in node.Manifest.Sprites)
            {
                if (!spriteNames.Add(sprite.Name))
                    throw new InvalidDataException($"Sprite '{sprite.Name}' appears in multiple packages.");
            }
            foreach (AnimationAssetDefinition animation in node.Manifest.Animations)
            {
                if (!animationNames.Add(animation.Name))
                    throw new InvalidDataException(
                        $"Animation '{animation.Name}' appears in multiple packages.");
            }
            foreach (AudioAssetDefinition audio in node.Manifest.AudioClips)
            {
                if (!audioNames.Add(audio.Name))
                    throw new InvalidDataException($"Audio clip '{audio.Name}' appears in multiple packages.");
                string path = ResolveUnderRoot(node.PackageDirectory, audio.Path, "Audio clip");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Audio asset '{audio.Path}' does not exist.", path);
                ValidateAudioFile(audio, path);
                string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
                string outputPath = NormalizeRelativePath(Path.Combine(relativeDirectory, audio.Path));
                if (StringComparer.OrdinalIgnoreCase.Equals(outputPath, MetadataFileName))
                    throw new InvalidDataException($"Audio clip '{audio.Name}' uses reserved build metadata path.");
            }
            foreach (TileSetAssetDefinition tileSet in node.Manifest.TileSets)
            {
                if (!tileSetNames.Add(tileSet.Name))
                    throw new InvalidDataException($"TileSet '{tileSet.Name}' appears in multiple packages.");
            }
            foreach (TileMapAssetDefinition tileMap in node.Manifest.TileMaps)
            {
                if (!tileMapNames.Add(tileMap.Name))
                    throw new InvalidDataException($"TileMap '{tileMap.Name}' appears in multiple packages.");
                string path = ResolveUnderRoot(node.PackageDirectory, tileMap.Path, "TileMap");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"TileMap asset '{tileMap.Path}' does not exist.", path);
                string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
                string outputPath = NormalizeRelativePath(Path.Combine(relativeDirectory, tileMap.Path));
                if (StringComparer.OrdinalIgnoreCase.Equals(outputPath, MetadataFileName))
                    throw new InvalidDataException($"TileMap '{tileMap.Name}' uses reserved build metadata path.");
            }
            foreach (TileWorldAssetDefinition tileWorld in node.Manifest.TileWorlds)
            {
                if (!tileWorldNames.Add(tileWorld.Name))
                    throw new InvalidDataException($"TileWorld '{tileWorld.Name}' appears in multiple packages.");
                if (tileWorld.Build is null)
                    throw new InvalidDataException($"Source TileWorld '{tileWorld.Name}' requires build settings.");
                string path = ResolveUnderRoot(node.PackageDirectory, tileWorld.Path, "TileWorld source");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"TileWorld source '{tileWorld.Path}' does not exist.", path);
                string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
                string outputPath = NormalizeRelativePath(Path.Combine(
                    relativeDirectory, ContentAssetCompiler.CompiledTileWorldPath(tileWorld.Path)));
                if (StringComparer.OrdinalIgnoreCase.Equals(outputPath, MetadataFileName))
                    throw new InvalidDataException($"TileWorld '{tileWorld.Name}' uses reserved build metadata path.");
            }
        }

        foreach (var consumer in graph)
        {
            foreach (var sprite in consumer.Manifest.Sprites)
            {
                foreach (string textureName in EnumerateSpriteTextureNames(sprite))
                {
                    if (!textureOwners.TryGetValue(textureName, out var owner))
                        continue;
                    if (ReferenceEquals(owner, consumer))
                        continue;
                    if (owner.Manifest.Atlas?.Textures.Contains(textureName, StringComparer.Ordinal) == true)
                    {
                        throw new InvalidDataException(
                            $"Package '{consumer.Manifest.Id}' references build-only Atlas Texture " +
                            $"'{textureName}' owned by dependency '{owner.Manifest.Id}'. " +
                            "Reference the dependency Sprite instead, or remove that Texture from its Atlas inputs.");
                    }
                }
            }
            foreach (AnimationAssetDefinition animation in consumer.Manifest.Animations)
            {
                if (!HasVisibleSprite(consumer, animation.SpriteName, new HashSet<string>(PathComparer)))
                {
                    throw new InvalidDataException(
                        $"Animation '{animation.Name}' references Sprite '{animation.SpriteName}' " +
                        "outside its package dependency closure.");
                }
            }
            foreach (TileSetAssetDefinition tileSet in consumer.Manifest.TileSets)
            {
                foreach (TileAssetDefinition tile in tileSet.Tiles)
                {
                    if (!HasVisibleSprite(consumer, tile.SpriteName, new HashSet<string>(PathComparer)))
                    {
                        throw new InvalidDataException(
                            $"TileSet '{tileSet.Name}' references Sprite '{tile.SpriteName}' outside its package dependency closure.");
                    }
                }
            }
            foreach (TileMapAssetDefinition definition in consumer.Manifest.TileMaps)
            {
                string path = ResolveUnderRoot(consumer.PackageDirectory, definition.Path, "TileMap");
                using var stream = File.OpenRead(path);
                TileMap map = TileMapManifestParser.Parse(stream);
                if (!StringComparer.Ordinal.Equals(map.Name, definition.Name))
                    throw new InvalidDataException(
                        $"TileMap declaration '{definition.Name}' does not match document name '{map.Name}'.");
                foreach (TileLayer layer in map.Layers)
                {
                    if (!HasVisibleTileSet(consumer, layer.TileSet.Name, new HashSet<string>(PathComparer)))
                    {
                        throw new InvalidDataException(
                            $"TileMap '{map.Name}' references TileSet '{layer.TileSet}' outside its package dependency closure.");
                    }
                }
            }
            foreach (TileWorldAssetDefinition definition in consumer.Manifest.TileWorlds)
            {
                TileWorldAssetBuildDefinition build = definition.Build
                    ?? throw new InvalidDataException(
                        $"Source TileWorld '{definition.Name}' requires build settings.");
                if (build.LodCount > 1 && build.Encoding != AtlasPageEncoding.WebpLossless)
                    throw new InvalidDataException(
                        $"TileWorld '{definition.Name}' visual LODs require webpLossless encoding.");
                try
                {
                    _ = new TileWorldRasterSettings(
                        build.RasterChunkSize.Width,
                        build.RasterChunkSize.Height,
                        build.Gutter,
                        build.Sampling == TextureSampler.PixelArt
                            ? TileWorldRasterSampling.PixelArt
                            : TileWorldRasterSampling.Smooth);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new InvalidDataException(
                        $"TileWorld '{definition.Name}' has invalid raster build settings.", exception);
                }
                string path = ResolveUnderRoot(consumer.PackageDirectory, definition.Path, "TileWorld source");
                using var stream = File.OpenRead(path);
                TileMap map = TileMapManifestParser.Parse(stream);
                if (!StringComparer.Ordinal.Equals(map.Name, definition.Name))
                    throw new InvalidDataException(
                        $"TileWorld declaration '{definition.Name}' does not match document name '{map.Name}'.");
                System.Numerics.Vector2? commonTileSize = null;
                foreach (TileLayer layer in map.Layers)
                {
                    TileSetAssetDefinition? tileSet = FindVisibleTileSet(
                        consumer, layer.TileSet.Name, new HashSet<string>(PathComparer));
                    if (tileSet is null)
                        throw new InvalidDataException(
                            $"TileWorld '{map.Name}' references TileSet '{layer.TileSet}' outside its package dependency closure.");
                    if (commonTileSize is { } known && known != tileSet.TileSize)
                        throw new InvalidDataException(
                            $"TileWorld '{map.Name}' requires one common Tile size across streamed layers.");
                    commonTileSize = tileSet.TileSize;
                }
                foreach (TileWorldFallbackSurfaceAssetDefinition fallback in build.FallbackSurfaces)
                {
                    TileLayer? layer = map.Layers.FirstOrDefault(candidate =>
                        StringComparer.Ordinal.Equals(candidate.Name, fallback.Layer));
                    if (layer is null)
                        throw new InvalidDataException(
                            $"TileWorld '{map.Name}' fallback surface references unknown layer '{fallback.Layer}'.");
                    if (!layer.Visible)
                        throw new InvalidDataException(
                            $"TileWorld '{map.Name}' fallback surface layer '{fallback.Layer}' is hidden.");
                    string fallbackPath = ResolveUnderRoot(
                        consumer.PackageDirectory,
                        fallback.Path,
                        "TileWorld fallback surface");
                    using var fallbackStream = File.OpenRead(fallbackPath);
                    DecodedImage decoded = _imageDecoder.Decode(fallbackStream);
                    if (decoded.Width is <= 0 or > 16_384 || decoded.Height is <= 0 or > 16_384 ||
                        (long)decoded.Width * decoded.Height > 67_108_864L)
                        throw new InvalidDataException(
                            $"TileWorld '{map.Name}' fallback surface '{fallback.Path}' exceeds the pixel limit.");
                }
            }
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

    private static bool HasVisibleTileSet(
        GraphNode node,
        string tileSetName,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return false;
        if (node.Manifest.TileSets.Any(tileSet =>
                StringComparer.Ordinal.Equals(tileSet.Name, tileSetName)))
            return true;
        foreach (GraphNode dependency in node.Dependencies)
            if (HasVisibleTileSet(dependency, tileSetName, visited)) return true;
        return false;
    }

    private static TileSetAssetDefinition? FindVisibleTileSet(
        GraphNode node,
        string tileSetName,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return null;
        TileSetAssetDefinition? local = node.Manifest.TileSets.FirstOrDefault(tileSet =>
            StringComparer.Ordinal.Equals(tileSet.Name, tileSetName));
        if (local is not null) return local;
        foreach (GraphNode dependency in node.Dependencies)
        {
            TileSetAssetDefinition? found = FindVisibleTileSet(dependency, tileSetName, visited);
            if (found is not null) return found;
        }
        return null;
    }

    private static bool HasVisibleSprite(
        GraphNode node,
        string spriteName,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return false;
        if (node.Manifest.Sprites.Any(sprite =>
                StringComparer.Ordinal.Equals(sprite.Name, spriteName)))
            return true;
        foreach (GraphNode dependency in node.Dependencies)
        {
            if (HasVisibleSprite(dependency, spriteName, visited)) return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateSpriteTextureNames(SpriteAssetDefinition sprite)
    {
        if (sprite.Layout is SpriteAssetLayout.Single or SpriteAssetLayout.Grid)
        {
            yield return sprite.TextureName!;
            yield break;
        }
        foreach (var frame in sprite.Frames)
            yield return frame.TextureName ?? sprite.TextureName!;
    }

    private static IReadOnlyDictionary<string, string> ComputePackageFingerprints(
        string packagesRoot,
        IReadOnlyList<GraphNode> graph)
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);

        string Compute(GraphNode node)
        {
            if (fingerprints.TryGetValue(node.Manifest.Id, out string? known))
                return known;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendString(hash, OwnerName);
            AppendString(hash, CompilerVersion);
            AppendString(hash, node.Manifest.Id);
            AppendString(hash, node.RelativeManifestPath);
            AppendFile(hash, node.ManifestPath);

            foreach (var texture in node.Manifest.Textures
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string source = ResolveUnderRoot(node.PackageDirectory, texture.Path, "Texture");
                string relative = NormalizeRelativePath(Path.GetRelativePath(packagesRoot, source));
                AppendString(hash, texture.Name);
                AppendString(hash, relative);
                AppendFile(hash, source);
            }

            foreach (AudioAssetDefinition audio in node.Manifest.AudioClips
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string source = ResolveUnderRoot(node.PackageDirectory, audio.Path, "Audio clip");
                string relative = NormalizeRelativePath(Path.GetRelativePath(packagesRoot, source));
                AppendString(hash, audio.Name);
                AppendString(hash, relative);
                AppendFile(hash, source);
            }

            foreach (TileMapAssetDefinition tileMap in node.Manifest.TileMaps
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string source = ResolveUnderRoot(node.PackageDirectory, tileMap.Path, "TileMap");
                string relative = NormalizeRelativePath(Path.GetRelativePath(packagesRoot, source));
                AppendString(hash, tileMap.Name);
                AppendString(hash, relative);
                AppendFile(hash, source);
            }

            foreach (TileWorldAssetDefinition tileWorld in node.Manifest.TileWorlds
                         .OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                string source = ResolveUnderRoot(node.PackageDirectory, tileWorld.Path, "TileWorld source");
                string relative = NormalizeRelativePath(Path.GetRelativePath(packagesRoot, source));
                AppendString(hash, tileWorld.Name);
                AppendString(hash, relative);
                AppendFile(hash, source);
                if (tileWorld.Build is { } build)
                {
                    foreach (TileWorldFallbackSurfaceAssetDefinition fallback in
                             build.FallbackSurfaces.OrderBy(item => item.Layer, StringComparer.Ordinal))
                    {
                        string fallbackSource = ResolveUnderRoot(
                            node.PackageDirectory,
                            fallback.Path,
                            "TileWorld fallback surface");
                        AppendString(hash, fallback.Layer);
                        AppendString(hash, NormalizeRelativePath(
                            Path.GetRelativePath(packagesRoot, fallbackSource)));
                        AppendFile(hash, fallbackSource);
                    }
                }
            }

            foreach (var dependency in node.Dependencies)
            {
                AppendString(hash, dependency.Manifest.Id);
                AppendString(hash, Compute(dependency));
            }

            string fingerprint = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            fingerprints.Add(node.Manifest.Id, fingerprint);
            return fingerprint;
        }

        foreach (var node in graph)
            Compute(node);
        return fingerprints;
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        using var stream = File.OpenRead(path);
        byte[] buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, read);
    }

    private static void CopyRawPackage(string staging, GraphNode node)
    {
        string manifestTarget = ResolveOutputPath(staging, node.RelativeManifestPath);
        CopyFileExclusive(node.ManifestPath, manifestTarget);
        string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
        foreach (var texture in node.Manifest.Textures)
        {
            string source = ResolveUnderRoot(node.PackageDirectory, texture.Path, "Texture");
            string target = ResolveOutputPath(staging, Path.Combine(relativeDirectory, texture.Path));
            CopyFileExclusive(source, target);
        }
        foreach (AudioAssetDefinition audio in node.Manifest.AudioClips)
        {
            string source = ResolveUnderRoot(node.PackageDirectory, audio.Path, "Audio clip");
            string target = ResolveOutputPath(staging, Path.Combine(relativeDirectory, audio.Path));
            CopyFileExclusive(source, target);
        }
        foreach (TileMapAssetDefinition tileMap in node.Manifest.TileMaps)
        {
            string source = ResolveUnderRoot(node.PackageDirectory, tileMap.Path, "TileMap");
            string target = ResolveOutputPath(staging, Path.Combine(relativeDirectory, tileMap.Path));
            CopyFileExclusive(source, target);
        }
    }

    private (int WorldCount, int ChunkCount, int RasterChunkCount) CompileTileWorlds(
        string staging,
        GraphNode node)
    {
        if (node.Manifest.TileWorlds.Count == 0) return (0, 0, 0);
        var tileSets = new TileSetLibrary();
        AddVisibleTileSets(node, tileSets, new HashSet<string>(PathComparer));
        TileWorldRasterSpriteSource? rasterSource = node.Manifest.TileWorlds.Any(world =>
            world.Build?.LodCount > 1)
            ? CreateRasterSource(node)
            : null;
        string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
        int chunks = 0;
        int rasterChunkCount = 0;
        foreach (TileWorldAssetDefinition definition in node.Manifest.TileWorlds)
        {
            TileWorldAssetBuildDefinition buildDefinition = definition.Build
                ?? throw new InvalidDataException($"Source TileWorld '{definition.Name}' requires build settings.");
            string source = ResolveUnderRoot(node.PackageDirectory, definition.Path, "TileWorld source");
            using var sourceStream = File.OpenRead(source);
            TileMap map = TileMapManifestParser.Parse(sourceStream);
            var rasterSettings = new TileWorldRasterSettings(
                buildDefinition.RasterChunkSize.Width,
                buildDefinition.RasterChunkSize.Height,
                buildDefinition.Gutter,
                buildDefinition.Sampling == TextureSampler.PixelArt
                    ? TileWorldRasterSampling.PixelArt
                    : TileWorldRasterSampling.Smooth);
            TileWorldArchiveBuild lod0 = TileWorldArchiveBuilder.BuildLod0(
                map,
                tileSets,
                buildDefinition.Bounds,
                buildDefinition.LodCount,
                rasterSettings);
            var rasterChunks = new List<TileWorldRasterChunkData>();
            if (buildDefinition.LodCount > 1)
            {
                foreach (TileWorldRasterChunkImage image in TileWorldRasterizer.RasterizeLodLevels(
                             map, tileSets, lod0.Metadata, rasterSource!))
                {
                    rasterChunks.Add(new TileWorldRasterChunkData(
                        image.Key,
                        image.Layers.Select(layer => new TileWorldRasterLayerData(
                            layer.LayerIndex,
                            layer.Width,
                            layer.Height,
                            layer.Gutter,
                            TileWorldRasterEncoding.WebpLossless,
                            TileWorldLosslessWebpEncoder.Encode(layer)))));
                }
            }
            var fallbackSurfaces = new List<TileWorldFallbackSurfaceData>(
                buildDefinition.FallbackSurfaces.Count);
            foreach (TileWorldFallbackSurfaceAssetDefinition fallback in
                     buildDefinition.FallbackSurfaces.OrderBy(item => item.Layer, StringComparer.Ordinal))
            {
                int layerIndex = -1;
                for (int index = 0; index < lod0.Metadata.Layers.Count; index++)
                {
                    if (!StringComparer.Ordinal.Equals(lod0.Metadata.Layers[index].Name, fallback.Layer))
                        continue;
                    layerIndex = index;
                    break;
                }
                if (layerIndex < 0)
                    throw new InvalidDataException(
                        $"TileWorld '{definition.Name}' fallback surface references unknown layer '{fallback.Layer}'.");
                string fallbackPath = ResolveUnderRoot(
                    node.PackageDirectory,
                    fallback.Path,
                    "TileWorld fallback surface");
                using var fallbackStream = File.OpenRead(fallbackPath);
                DecodedImage decoded = _imageDecoder.Decode(fallbackStream);
                fallbackSurfaces.Add(new TileWorldFallbackSurfaceData(
                    layerIndex,
                    decoded.Width,
                    decoded.Height,
                    TileWorldRasterEncoding.WebpLossless,
                    fallback.Sampling == TextureSampler.PixelArt
                        ? TileWorldRasterSampling.PixelArt
                        : TileWorldRasterSampling.Smooth,
                    TileWorldLosslessWebpEncoder.Encode(
                        decoded.Width,
                        decoded.Height,
                        decoded.RgbaPixels)));
            }
            var metadata = new TileWorldMetadata(
                lod0.Metadata.Name,
                lod0.Metadata.ChunkWidth,
                lod0.Metadata.ChunkHeight,
                lod0.Metadata.TileSize,
                lod0.Metadata.Bounds,
                lod0.Metadata.DeclaredLodCount,
                lod0.Metadata.RasterSettings,
                lod0.Metadata.Layers,
                fallbackSurfaces.Select(surface => surface.Metadata));
            var archive = new TileWorldArchiveBuild(
                metadata,
                lod0.Chunks,
                rasterChunks,
                fallbackSurfaces);
            string compiledRelative = ContentAssetCompiler.CompiledTileWorldPath(definition.Path);
            string output = ResolveOutputPath(staging, Path.Combine(relativeDirectory, compiledRelative));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (File.Exists(output))
                throw new InvalidDataException(
                    $"TileWorld output path '{compiledRelative}' is already produced by another asset.");
            using var destination = File.Create(output);
            TileWorldArchiveWriter.Write(destination, archive);
            chunks += archive.TotalChunkCount;
            rasterChunkCount += archive.RasterChunks.Count;
        }
        RewriteCompiledTileWorldManifest(
            ResolveOutputPath(staging, node.RelativeManifestPath), node.Manifest.TileWorlds);
        return (node.Manifest.TileWorlds.Count, chunks, rasterChunkCount);
    }

    private TileWorldRasterSpriteSource CreateRasterSource(GraphNode node)
    {
        var textures = new Dictionary<string, TileWorldRasterTextureInput>(StringComparer.Ordinal);
        var sprites = new Dictionary<string, SpriteAssetDefinition>(StringComparer.Ordinal);
        AddVisibleRasterAssets(
            node,
            textures,
            sprites,
            new HashSet<string>(PathComparer));
        return new TileWorldRasterSpriteSource(sprites, textures, _imageDecoder);
    }

    private static void AddVisibleRasterAssets(
        GraphNode node,
        Dictionary<string, TileWorldRasterTextureInput> textures,
        Dictionary<string, SpriteAssetDefinition> sprites,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return;
        foreach (GraphNode dependency in node.Dependencies)
            AddVisibleRasterAssets(dependency, textures, sprites, visited);
        foreach (TextureAssetDefinition definition in node.Manifest.Textures)
        {
            string path = ResolveUnderRoot(node.PackageDirectory, definition.Path, "Texture");
            if (!textures.TryAdd(
                    definition.Name,
                    new TileWorldRasterTextureInput(definition, path)))
                throw new InvalidDataException(
                    $"Texture '{definition.Name}' appears more than once in the TileWorld dependency closure.");
        }
        foreach (SpriteAssetDefinition definition in node.Manifest.Sprites)
        {
            if (!sprites.TryAdd(definition.Name, definition))
                throw new InvalidDataException(
                    $"Sprite '{definition.Name}' appears more than once in the TileWorld dependency closure.");
        }
    }

    private static void AddVisibleTileSets(
        GraphNode node,
        TileSetLibrary library,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return;
        foreach (GraphNode dependency in node.Dependencies)
            AddVisibleTileSets(dependency, library, visited);
        foreach (TileSetAssetDefinition definition in node.Manifest.TileSets)
        {
            if (library.TryGet(new TileSetRef(definition.Name), out _)) continue;
            library.Register(new TileSet(
                definition.Name,
                definition.TileSize,
                definition.Tiles.Select(tile => new TileDefinition(
                    new TileId(tile.Id),
                    new GameEngine.Core.Domain.ValueObjects.SpriteRef(tile.SpriteName),
                    tile.SubImage,
                    tile.Collision))));
        }
    }

    private static void RewriteCompiledTileWorldManifest(
        string manifestPath,
        IReadOnlyList<TileWorldAssetDefinition> definitions)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("Compiled package manifest is empty.");
        JsonObject rootObject = root as JsonObject
            ?? throw new InvalidDataException("Compiled package manifest root is invalid.");
        KeyValuePair<string, JsonNode?> property = rootObject.FirstOrDefault(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.Key, "tileWorlds"));
        JsonArray array = property.Value as JsonArray
            ?? throw new InvalidDataException("Compiled package manifest omitted TileWorld declarations.");
        if (array.Count != definitions.Count)
            throw new InvalidDataException("Compiled package TileWorld declaration count changed unexpectedly.");
        for (int i = 0; i < definitions.Count; i++)
        {
            JsonObject item = array[i] as JsonObject
                ?? throw new InvalidDataException("Compiled package contains an invalid TileWorld declaration.");
            string pathProperty = item.Select(pair => pair.Key).FirstOrDefault(key =>
                StringComparer.OrdinalIgnoreCase.Equals(key, "path")) ?? "path";
            item[pathProperty] = ContentAssetCompiler.CompiledTileWorldPath(definitions[i].Path);
            string? buildProperty = item.Select(pair => pair.Key).FirstOrDefault(key =>
                StringComparer.OrdinalIgnoreCase.Equals(key, "build"));
            if (buildProperty is not null) item.Remove(buildProperty);
        }
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<OutputFileHash> HashPackageOutput(string staging, GraphNode node)
    {
        string relativeDirectory = Path.GetDirectoryName(node.RelativeManifestPath) ?? string.Empty;
        string packageOutput = ResolveOutputPath(staging, relativeDirectory);
        return Directory.GetFiles(packageOutput, "*", SearchOption.AllDirectories)
            .Select(path => new OutputFileHash(
                NormalizeRelativePath(Path.GetRelativePath(staging, path)),
                HashFile(path)))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool ArePackageOutputsValid(
        string output,
        PackageBuildMetadata package)
    {
        if (!Directory.Exists(output) || package.Outputs.Count == 0)
            return false;
        foreach (var file in package.Outputs)
        {
            string path = ResolveOutputPath(output, file.Path);
            if (!File.Exists(path) || !StringComparer.OrdinalIgnoreCase.Equals(
                    HashFile(path), file.Sha256))
                return false;
        }
        return true;
    }

    private static void CopyCachedPackageOutputs(
        string output,
        string staging,
        PackageBuildMetadata package)
    {
        foreach (var file in package.Outputs)
        {
            CopyFileExclusive(
                ResolveOutputPath(output, file.Path),
                ResolveOutputPath(staging, file.Path));
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(ResolveOutputPath(destination, relative));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            CopyFileExclusive(file, ResolveOutputPath(destination, relative));
        }
    }

    private static void CopyFileExclusive(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
            throw new InvalidDataException($"Build output path '{destination}' is produced more than once.");
        File.Copy(source, destination, overwrite: false);
    }

    private static List<OutputFileHash> HashOutputFiles(string output) =>
        Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => !PathComparer.Equals(
                path, Path.Combine(output, MetadataFileName)))
            .Select(path => new OutputFileHash(
                NormalizeRelativePath(Path.GetRelativePath(output, path)),
                HashFile(path)))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToList();

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteMetadata(string output, BuildMetadata metadata)
    {
        string path = Path.Combine(output, MetadataFileName);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, metadata, MetadataJsonOptions);
    }

    private static BuildMetadata? TryReadMetadata(string output)
    {
        string path = Path.Combine(output, MetadataFileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<BuildMetadata>(stream, MetadataJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUpToDate(
        string output,
        BuildMetadata? metadata,
        string packageId,
        string rootManifest,
        string fingerprint)
    {
        if (metadata is null ||
            metadata.SchemaVersion != MetadataSchemaVersion ||
            metadata.Owner != OwnerName ||
            metadata.CompilerVersion != CompilerVersion ||
            metadata.RootPackageId != packageId ||
            metadata.RootManifest != rootManifest ||
            metadata.InputFingerprint != fingerprint ||
            !Directory.Exists(output))
        {
            return false;
        }

        string[] actual = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => !PathComparer.Equals(
                path, Path.Combine(output, MetadataFileName)))
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(output, path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] expected = metadata.Outputs
            .Select(item => item.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal)) return false;

        foreach (var expectedFile in metadata.Outputs)
        {
            string path = ResolveOutputPath(output, expectedFile.Path);
            if (!File.Exists(path) || !StringComparer.OrdinalIgnoreCase.Equals(
                    HashFile(path), expectedFile.Sha256))
                return false;
        }
        return true;
    }

    private static void EnsureReplaceableOutput(string output, BuildMetadata? metadata)
    {
        if (!Directory.Exists(output)) return;
        if (!Directory.EnumerateFileSystemEntries(output).Any()) return;
        if (metadata?.SchemaVersion == MetadataSchemaVersion && metadata.Owner == OwnerName)
            return;
        throw new IOException(
            $"Build output '{output}' is non-empty and is not owned by {OwnerName}.");
    }

    private static void ReplaceOutputAtomically(string output, string staging, string backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        bool movedOld = false;
        try
        {
            if (Directory.Exists(output))
            {
                Directory.Move(output, backup);
                movedOld = true;
            }
            Directory.Move(staging, output);
        }
        catch
        {
            if (movedOld && !Directory.Exists(output) && Directory.Exists(backup))
                Directory.Move(backup, output);
            throw;
        }
        if (movedOld)
            DeleteDirectoryIfExists(backup);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void ValidateOutputBoundary(string packagesRoot, string output)
    {
        if (Path.GetPathRoot(output)?.TrimEnd(Path.DirectorySeparatorChar) ==
            output.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("Build output cannot be a filesystem root.", nameof(output));

        string relative = Path.GetRelativePath(packagesRoot, output);
        if (relative == "." ||
            (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
             relative != ".." &&
             !Path.IsPathRooted(relative)))
        {
            throw new ArgumentException(
                "Build output must be outside the source packages root.",
                nameof(output));
        }
    }

    private static string ResolveUnderRoot(string root, string relativePath, string kind)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{kind} paths must be non-empty and relative.");
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, full);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"{kind} path '{relativePath}' escapes its configured root.");
        }
        return full;
    }

    private static string ResolveOutputPath(string output, string relativePath) =>
        ResolveUnderRoot(output, string.IsNullOrEmpty(relativePath) ? "." : relativePath, "Output");

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static int PackageDepth(string relativeManifestPath)
    {
        string? directory = Path.GetDirectoryName(relativeManifestPath);
        if (string.IsNullOrEmpty(directory)) return 0;
        return directory.Count(character =>
            character == Path.DirectorySeparatorChar ||
            character == Path.AltDirectorySeparatorChar) + 1;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
