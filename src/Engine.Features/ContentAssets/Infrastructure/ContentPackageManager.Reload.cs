namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Animation;
using GameEngine.Features.Sprites.Domain;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

public sealed partial class ContentPackageManager
{
    private sealed record ReloadPackageSnapshot(
        string Id,
        string ManifestPath,
        string[] DependencyIds,
        string[] TextureNames,
        string[] SpriteNames,
        string[] AnimationNames);

    private sealed record ReloadSnapshot(
        long Generation,
        IReadOnlyDictionary<string, ReloadPackageSnapshot> Packages,
        string[] TextureNames,
        string[] SpriteNames,
        string[] AnimationNames);

    private long _revisionGeneration;

    /// <summary>
    /// 在后台读取并解码一个完整的编译修订。该阶段不上传 GPU 资源，也不改变当前资源映射。
    /// </summary>
    public Task<PreparedContentPackageReload> PrepareReloadAsync(
        ContentPackageRef package,
        CompiledContentRevision revision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(revision);
        ValidateRevisionIdentity(package, revision);
        ReloadSnapshot snapshot = CaptureReloadSnapshot(package);

        return Task.Run(
            () => PrepareReload(package, revision, snapshot, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// 在图形线程原子激活已准备的修订。失败时 Texture、Sprite 与包索引恢复到旧版本。
    /// </summary>
    public void CommitReload(PreparedContentPackageReload prepared)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(prepared);
        if (!ReferenceEquals(prepared.Owner, this))
            throw new InvalidOperationException("The prepared reload belongs to a different package manager.");
        if (prepared.BaseGeneration != _revisionGeneration)
            throw new InvalidOperationException("The loaded content graph changed after this reload was prepared.");
        if (Interlocked.Exchange(ref prepared.Consumed, 1) != 0)
            throw new InvalidOperationException("A prepared content reload can only be committed once.");

        PackageState root = GetLivePackage(prepared.Package.Id);
        string expectedRoot = ResolveUnderRoot(_packagesRoot, prepared.Package.Manifest, "Manifest");
        if (!PathComparer.Equals(root.ManifestPath, expectedRoot))
            throw new InvalidOperationException("The prepared reload no longer targets the loaded root package.");

        using var textureTransaction = _textures.BeginReplacement(
            prepared.ReplacedTextureNames,
            prepared.Textures);
        textureTransaction.Activate();

        using var spriteTransaction = _sprites.BeginReplacement(
            prepared.ReplacedSpriteNames,
            prepared.Sprites);
        spriteTransaction.Activate();

        using var animationTransaction = _animations.BeginReplacement(
            prepared.ReplacedAnimationNames,
            prepared.Animations);
        animationTransaction.Activate();

        var previous = new List<(
            PackageState State,
            IReadOnlyList<TextureRef> Textures,
            IReadOnlyList<SpriteRef> Sprites,
            IReadOnlyList<AnimationClipRef> Animations)>(
            prepared.Packages.Count);
        try
        {
            foreach (PreparedPackageRevision package in prepared.Packages)
            {
                PackageState state = GetLivePackage(package.Id);
                if (!PathComparer.Equals(state.ManifestPath, package.ManifestPath))
                    throw new InvalidOperationException($"Package '{package.Id}' changed while reload was prepared.");
                previous.Add((state, state.Textures, state.Sprites, state.Animations));
                state.Textures = package.Textures;
                state.Sprites = package.Sprites;
                state.Animations = package.Animations;
            }

            animationTransaction.Commit();
            spriteTransaction.Commit();
            textureTransaction.Commit();
            _revisionGeneration++;
        }
        catch
        {
            for (int i = previous.Count - 1; i >= 0; i--)
            {
                previous[i].State.Textures = previous[i].Textures;
                previous[i].State.Sprites = previous[i].Sprites;
                previous[i].State.Animations = previous[i].Animations;
            }
            throw;
        }
    }

    private PreparedContentPackageReload PrepareReload(
        ContentPackageRef package,
        CompiledContentRevision expectedRevision,
        ReloadSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CompiledContentRevision before = CompiledContentRevisionReader.Read(_packagesRoot, package);
        if (before != expectedRevision)
            throw new InvalidOperationException("The compiled content revision changed before preparation began.");

        string rootPath = ResolveUnderRoot(_packagesRoot, package.Manifest, "Manifest");
        var nodesByPath = new Dictionary<string, GraphNode>(PathComparer);
        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        GraphNode root = ReadReloadGraph(
            _packagesRoot,
            rootPath,
            package.Id,
            nodesByPath,
            nodesById,
            new HashSet<string>(PathComparer),
            cancellationToken);
        ValidateReloadTopology(snapshot, nodesById);

        GraphNode[] ordered = TopologicalOrder(root);
        if (ordered.Any(node => node.Manifest.AudioClips.Count > 0))
        {
            throw new NotSupportedException(
                "Audio clip hot reload is not part of the short-SFX slice. Reload the application to replace audio assets.");
        }
        var decoder = new SkiaImageDecoder();
        var textures = new List<TextureReplacementSource>();
        var textureMetadata = new Dictionary<string, TextureMetadata>(StringComparer.Ordinal);
        var packageTextures = new Dictionary<string, TextureRef[]>(StringComparer.Ordinal);

        foreach (GraphNode node in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = Path.GetDirectoryName(node.ManifestPath)!;
            var refs = new TextureRef[node.Manifest.Textures.Count];
            for (int i = 0; i < refs.Length; i++)
            {
                TextureAssetDefinition definition = node.Manifest.Textures[i];
                string imagePath = ResolveUnderRoot(directory, definition.Path, "Texture");
                using var stream = File.OpenRead(imagePath);
                DecodedImage image = decoder.Decode(stream);
                if (!textureMetadata.TryAdd(
                        definition.Name,
                        new TextureMetadata(image.Width, image.Height)))
                    throw new InvalidDataException($"Texture '{definition.Name}' appears more than once in the graph.");
                textures.Add(new TextureReplacementSource(
                    definition.Name,
                    image.Width,
                    image.Height,
                    image.RgbaPixels,
                    definition.Sampler));
                refs[i] = new TextureRef(definition.Name);
            }
            packageTextures.Add(node.Manifest.Id, refs);
        }

        var sprites = new List<SpriteReplacementSource>();
        var spriteNames = new HashSet<string>(StringComparer.Ordinal);
        var spriteFrameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var animations = new List<AnimationReplacementSource>();
        var animationNames = new HashSet<string>(StringComparer.Ordinal);
        var preparedPackages = new List<PreparedPackageRevision>(ordered.Length);
        foreach (GraphNode node in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<string> visibleTextures = CollectReloadVisibleTextures(node);
            var spriteRefs = new SpriteRef[node.Manifest.Sprites.Count];
            for (int i = 0; i < spriteRefs.Length; i++)
            {
                SpriteAssetDefinition definition = node.Manifest.Sprites[i];
                if (!spriteNames.Add(definition.Name))
                    throw new InvalidDataException($"Sprite '{definition.Name}' appears more than once in the graph.");
                SpriteReplacementSource sprite = BuildReloadSprite(
                    definition,
                    visibleTextures,
                    textureMetadata);
                sprites.Add(sprite);
                spriteFrameCounts.Add(definition.Name, sprite.Frames.Length);
                spriteRefs[i] = new SpriteRef(definition.Name);
            }
            HashSet<string> visibleSprites = CollectReloadVisibleSprites(node);
            var animationRefs = new AnimationClipRef[node.Manifest.Animations.Count];
            for (int i = 0; i < animationRefs.Length; i++)
            {
                AnimationAssetDefinition definition = node.Manifest.Animations[i];
                if (!animationNames.Add(definition.Name))
                    throw new InvalidDataException(
                        $"Animation '{definition.Name}' appears more than once in the graph.");
                animations.Add(BuildReloadAnimation(
                    definition,
                    visibleSprites,
                    spriteFrameCounts));
                animationRefs[i] = new AnimationClipRef(definition.Name);
            }
            preparedPackages.Add(new PreparedPackageRevision(
                node.Manifest.Id,
                node.ManifestPath,
                packageTextures[node.Manifest.Id],
                spriteRefs,
                animationRefs));
        }

        CompiledContentRevision after = CompiledContentRevisionReader.Read(_packagesRoot, package);
        if (after != expectedRevision)
            throw new InvalidOperationException("The compiled content revision changed while it was being prepared.");

        return new PreparedContentPackageReload(
            this,
            package,
            after,
            snapshot.Generation,
            preparedPackages,
            snapshot.TextureNames,
            snapshot.SpriteNames,
            snapshot.AnimationNames,
            textures,
            sprites,
            animations);
    }

    private ReloadSnapshot CaptureReloadSnapshot(ContentPackageRef package)
    {
        PackageState root = GetLivePackage(package.Id);
        string expectedPath = ResolveUnderRoot(_packagesRoot, package.Manifest, "Manifest");
        if (!PathComparer.Equals(root.ManifestPath, expectedPath))
            throw new InvalidOperationException($"Package '{package.Id}' is loaded from a different manifest.");

        var packages = new Dictionary<string, ReloadPackageSnapshot>(StringComparer.Ordinal);
        var textures = new HashSet<string>(StringComparer.Ordinal);
        var sprites = new HashSet<string>(StringComparer.Ordinal);
        var animations = new HashSet<string>(StringComparer.Ordinal);
        Capture(root);
        return new ReloadSnapshot(
            _revisionGeneration,
            packages,
            textures.Order(StringComparer.Ordinal).ToArray(),
            sprites.Order(StringComparer.Ordinal).ToArray(),
            animations.Order(StringComparer.Ordinal).ToArray());

        void Capture(PackageState state)
        {
            if (packages.ContainsKey(state.Id)) return;
            foreach (PackageState dependency in state.Dependencies) Capture(dependency);
            string[] textureNames = state.Textures.Select(item => item.Name).ToArray();
            string[] spriteNames = state.Sprites.Select(item => item.Name).ToArray();
            string[] animationNames = state.Animations.Select(item => item.Name).ToArray();
            foreach (string name in textureNames) textures.Add(name);
            foreach (string name in spriteNames) sprites.Add(name);
            foreach (string name in animationNames) animations.Add(name);
            packages.Add(state.Id, new ReloadPackageSnapshot(
                state.Id,
                state.ManifestPath,
                state.Dependencies.Select(item => item.Id).Order(StringComparer.Ordinal).ToArray(),
                textureNames,
                spriteNames,
                animationNames));
        }
    }

    private static GraphNode ReadReloadGraph(
        string packagesRoot,
        string manifestPath,
        string? expectedId,
        Dictionary<string, GraphNode> nodesByPath,
        Dictionary<string, GraphNode> nodesById,
        HashSet<string> visiting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (nodesByPath.TryGetValue(manifestPath, out GraphNode? known))
        {
            if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, known.Manifest.Id))
                throw DependencyIdMismatch(expectedId, known.Manifest.Id, manifestPath);
            if (visiting.Contains(manifestPath))
                throw new InvalidDataException($"Content package dependency cycle reaches '{known.Manifest.Id}'.");
            return known;
        }

        using var stream = File.OpenRead(manifestPath);
        AssetPackageManifest manifest = AssetPackageManifestParser.Parse(stream);
        if (expectedId is not null && !StringComparer.Ordinal.Equals(expectedId, manifest.Id))
            throw DependencyIdMismatch(expectedId, manifest.Id, manifestPath);
        if (nodesById.TryGetValue(manifest.Id, out GraphNode? duplicate) &&
            !PathComparer.Equals(duplicate.ManifestPath, manifestPath))
            throw new InvalidDataException($"Package id '{manifest.Id}' resolves to multiple manifests.");

        var node = new GraphNode { ManifestPath = manifestPath, Manifest = manifest };
        nodesByPath.Add(manifestPath, node);
        nodesById.Add(manifest.Id, node);
        visiting.Add(manifestPath);
        try
        {
            foreach (AssetPackageDependency dependency in manifest.Dependencies)
            {
                // Dependency manifests are always relative to the packages root, not the package directory.
                string dependencyPath = ResolveUnderRoot(
                    packagesRoot,
                    dependency.Manifest,
                    "Dependency manifest");
                node.Dependencies.Add(ReadReloadGraph(
                    packagesRoot,
                    dependencyPath,
                    dependency.Id,
                    nodesByPath,
                    nodesById,
                    visiting,
                    cancellationToken));
            }
        }
        finally
        {
            visiting.Remove(manifestPath);
        }
        return node;
    }

    private static void ValidateReloadTopology(
        ReloadSnapshot snapshot,
        IReadOnlyDictionary<string, GraphNode> nodes)
    {
        if (snapshot.Packages.Count != nodes.Count)
            throw new InvalidOperationException("Content hot reload does not change package dependency topology in v1.");
        foreach ((string id, ReloadPackageSnapshot oldPackage) in snapshot.Packages)
        {
            if (!nodes.TryGetValue(id, out GraphNode? node) ||
                !PathComparer.Equals(oldPackage.ManifestPath, node.ManifestPath) ||
                !oldPackage.DependencyIds.SequenceEqual(
                    node.Dependencies.Select(item => item.Manifest.Id).Order(StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Content hot reload does not change package IDs, paths, or dependency topology in v1.");
            }
        }
    }

    private static GraphNode[] TopologicalOrder(GraphNode root)
    {
        var result = new List<GraphNode>();
        var visited = new HashSet<string>(PathComparer);
        Visit(root);
        return result.ToArray();

        void Visit(GraphNode node)
        {
            if (!visited.Add(node.ManifestPath)) return;
            foreach (GraphNode dependency in node.Dependencies) Visit(dependency);
            result.Add(node);
        }
    }

    private static HashSet<string> CollectReloadVisibleTextures(GraphNode root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(PathComparer);
        Visit(root);
        return result;

        void Visit(GraphNode node)
        {
            if (!visited.Add(node.ManifestPath)) return;
            foreach (TextureAssetDefinition texture in node.Manifest.Textures) result.Add(texture.Name);
            foreach (GraphNode dependency in node.Dependencies) Visit(dependency);
        }
    }

    private static HashSet<string> CollectReloadVisibleSprites(GraphNode root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(PathComparer);
        Visit(root);
        return result;

        void Visit(GraphNode node)
        {
            if (!visited.Add(node.ManifestPath)) return;
            foreach (SpriteAssetDefinition sprite in node.Manifest.Sprites) result.Add(sprite.Name);
            foreach (GraphNode dependency in node.Dependencies) Visit(dependency);
        }
    }

    private static AnimationReplacementSource BuildReloadAnimation(
        AnimationAssetDefinition definition,
        HashSet<string> visibleSprites,
        IReadOnlyDictionary<string, int> spriteFrameCounts)
    {
        if (!visibleSprites.Contains(definition.SpriteName) ||
            !spriteFrameCounts.TryGetValue(definition.SpriteName, out int spriteFrameCount))
        {
            throw new InvalidDataException(
                $"Animation '{definition.Name}' references Sprite '{definition.SpriteName}' " +
                "outside its dependency closure.");
        }
        for (int i = 0; i < definition.Frames.Count; i++)
        {
            if ((uint)definition.Frames[i] >= (uint)spriteFrameCount)
            {
                throw new InvalidDataException(
                    $"Animation '{definition.Name}' frame {i} exceeds Sprite '{definition.SpriteName}'.");
            }
        }
        return new AnimationReplacementSource(
            definition.Name,
            new SpriteRef(definition.SpriteName),
            definition.Frames.ToArray(),
            definition.FramesPerSecond,
            definition.LoopMode,
            definition.Markers.Select(marker => new AnimationFrameMarker(
                marker.Frame,
                new AnimationEventRef(marker.Event))).ToArray());
    }

    private static SpriteReplacementSource BuildReloadSprite(
        SpriteAssetDefinition definition,
        HashSet<string> visibleTextures,
        IReadOnlyDictionary<string, TextureMetadata> textureMetadata)
    {
        SpriteFrameSource[] frames = definition.Layout switch
        {
            SpriteAssetLayout.Single => BuildSingle(),
            SpriteAssetLayout.Grid => BuildGrid(),
            SpriteAssetLayout.Frames => BuildFrames(),
            _ => throw new InvalidDataException($"Unsupported Sprite layout '{definition.Layout}'.")
        };
        ValidateFrames(frames);
        Vector2 logicalSize = definition.LogicalSize ??
            new Vector2(frames[0].SourceRect.Width, frames[0].SourceRect.Height);
        if (!float.IsFinite(logicalSize.X) || !float.IsFinite(logicalSize.Y) ||
            logicalSize.X <= 0 || logicalSize.Y <= 0 ||
            !float.IsFinite(definition.Origin.X) || !float.IsFinite(definition.Origin.Y) ||
            !float.IsFinite(definition.FramesPerSecond) || definition.FramesPerSecond < 0)
            throw new InvalidDataException($"Sprite '{definition.Name}' has invalid runtime metadata.");
        return new SpriteReplacementSource(
            definition.Name,
            logicalSize,
            definition.Origin,
            frames,
            definition.FramesPerSecond);

        SpriteFrameSource[] BuildSingle()
        {
            TextureRef texture = RequireTexture(definition.TextureName!);
            return [new SpriteFrameSource(texture, definition.SourceRect ?? FullRect(texture))];
        }

        SpriteFrameSource[] BuildGrid()
        {
            TextureRef texture = RequireTexture(definition.TextureName!);
            TextureMetadata metadata = textureMetadata[texture.Name];
            PixelSizeI size = definition.FrameSize!.Value;
            int count = definition.FrameCount!.Value;
            int columns = metadata.Width / size.Width;
            int rows = metadata.Height / size.Height;
            if (columns <= 0 || rows <= 0 || count > checked(columns * rows))
                throw new InvalidDataException($"Grid Sprite '{definition.Name}' exceeds Texture '{texture.Name}'.");
            var result = new SpriteFrameSource[count];
            for (int i = 0; i < count; i++)
                result[i] = new SpriteFrameSource(texture, new PixelRectI(
                    (i % columns) * size.Width,
                    (i / columns) * size.Height,
                    size.Width,
                    size.Height));
            return result;
        }

        SpriteFrameSource[] BuildFrames()
        {
            var result = new SpriteFrameSource[definition.Frames.Count];
            for (int i = 0; i < result.Length; i++)
            {
                SpriteAssetFrameDefinition frame = definition.Frames[i];
                TextureRef texture = RequireTexture(frame.TextureName ?? definition.TextureName!);
                result[i] = new SpriteFrameSource(texture, frame.SourceRect ?? FullRect(texture));
            }
            return result;
        }

        TextureRef RequireTexture(string name)
        {
            if (!visibleTextures.Contains(name) || !textureMetadata.ContainsKey(name))
                throw new InvalidDataException(
                    $"Sprite '{definition.Name}' references Texture '{name}' outside its dependency closure.");
            return new TextureRef(name);
        }

        PixelRectI FullRect(TextureRef texture)
        {
            TextureMetadata metadata = textureMetadata[texture.Name];
            return new PixelRectI(0, 0, metadata.Width, metadata.Height);
        }

        void ValidateFrames(SpriteFrameSource[] sources)
        {
            if (sources.Length == 0)
                throw new InvalidDataException($"Sprite '{definition.Name}' has no frames.");
            int width = sources[0].SourceRect.Width;
            int height = sources[0].SourceRect.Height;
            foreach (SpriteFrameSource source in sources)
            {
                PixelRectI rect = source.SourceRect;
                TextureMetadata metadata = textureMetadata[source.Texture.Name];
                if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                    (long)rect.X + rect.Width > metadata.Width ||
                    (long)rect.Y + rect.Height > metadata.Height ||
                    rect.Width != width || rect.Height != height)
                    throw new InvalidDataException($"Sprite '{definition.Name}' contains invalid or inconsistent frames.");
            }
        }
    }

    private static void ValidateRevisionIdentity(
        ContentPackageRef package,
        CompiledContentRevision revision)
    {
        if (!StringComparer.Ordinal.Equals(package.Id, revision.PackageId) ||
            !StringComparer.Ordinal.Equals(
                package.Manifest.Replace('\\', '/').TrimStart('/'),
                revision.RootManifest.Replace('\\', '/').TrimStart('/')))
            throw new ArgumentException("Revision does not describe the requested content package.", nameof(revision));
    }

}
