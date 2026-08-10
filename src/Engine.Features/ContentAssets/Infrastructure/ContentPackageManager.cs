namespace GameEngine.Features.ContentAssets.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>
/// Synchronously loads versioned content packages and owns only the resources assembled by them.
/// All methods must run on the graphics-context thread.
/// </summary>
public sealed partial class ContentPackageManager : IDisposable
{
    private sealed class GraphNode
    {
        public required string ManifestPath { get; init; }
        public required AssetPackageManifest Manifest { get; init; }
        public List<GraphNode> Dependencies { get; } = [];
    }

    private sealed class PackageState
    {
        public required string Id { get; init; }
        public required string ManifestPath { get; init; }
        public required IReadOnlyList<TextureRef> Textures { get; set; }
        public required IReadOnlyList<SpriteRef> Sprites { get; set; }
        public required IReadOnlyList<AnimationClipRef> Animations { get; set; }
        public required IReadOnlyList<AudioClipRef> AudioClips { get; set; }
        public required IReadOnlyList<PackageState> Dependencies { get; init; }
        public required long LoadOrder { get; init; }
        public int ReferenceCount { get; set; }
    }

    private readonly TextureLibrary _textures;
    private readonly SpriteLibrary _sprites;
    private readonly AnimationLibrary _animations;
    private readonly AudioLibrary _audio;
    private readonly string _packagesRoot;
    private readonly Dictionary<string, PackageState> _packagesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PackageState> _packagesByPath = new(PathComparer);
    private long _nextLoadOrder;
    private bool _disposed;

    public ContentPackageManager(
        TextureLibrary textureLibrary,
        SpriteLibrary spriteLibrary,
        string packagesRoot)
        : this(textureLibrary, spriteLibrary, new AnimationLibrary(), new AudioLibrary(), packagesRoot)
    {
    }

    public ContentPackageManager(
        TextureLibrary textureLibrary,
        SpriteLibrary spriteLibrary,
        AnimationLibrary animationLibrary,
        string packagesRoot)
        : this(textureLibrary, spriteLibrary, animationLibrary, new AudioLibrary(), packagesRoot)
    {
    }

    public ContentPackageManager(
        TextureLibrary textureLibrary,
        SpriteLibrary spriteLibrary,
        AnimationLibrary animationLibrary,
        AudioLibrary audioLibrary,
        string packagesRoot)
    {
        _textures = textureLibrary ?? throw new ArgumentNullException(nameof(textureLibrary));
        _sprites = spriteLibrary ?? throw new ArgumentNullException(nameof(spriteLibrary));
        _animations = animationLibrary ?? throw new ArgumentNullException(nameof(animationLibrary));
        _audio = audioLibrary ?? throw new ArgumentNullException(nameof(audioLibrary));
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesRoot);

        _packagesRoot = Path.GetFullPath(packagesRoot);
        if (!Directory.Exists(_packagesRoot))
            throw new DirectoryNotFoundException($"Packages root '{_packagesRoot}' does not exist.");
    }

    public int LoadedPackageCount => _packagesById.Count;
    public string PackagesRoot => _packagesRoot;
    public AnimationLibrary Animations => _animations;
    public AudioLibrary Audio => _audio;

    public LoadedContentPackage Load(string rootRelativeManifestPath) =>
        LoadCore(rootRelativeManifestPath, expectedId: null);

    public LoadedContentPackage Load(ContentPackageRef package) =>
        LoadCore(package.Manifest, package.Id);

    private LoadedContentPackage LoadCore(string rootRelativeManifestPath, string? expectedId)
    {
        ThrowIfDisposed();
        string rootPath = ResolveUnderRoot(_packagesRoot, rootRelativeManifestPath, "Manifest");

        if (_packagesByPath.TryGetValue(rootPath, out var cached))
        {
            if (expectedId is not null &&
                !StringComparer.Ordinal.Equals(expectedId, cached.Id))
            {
                throw new InvalidDataException(
                    $"Content package reference expected '{expectedId}', but manifest " +
                    $"'{rootRelativeManifestPath}' contains '{cached.Id}'.");
            }
            checked { cached.ReferenceCount++; }
            return new LoadedContentPackage(this, cached.Id);
        }

        var nodesByPath = new Dictionary<string, GraphNode>(PathComparer);
        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(PathComparer);
        GraphNode root = ReadGraph(rootPath, expectedId, nodesByPath, nodesById, visiting);
        ValidateGraphBeforeLoading(nodesById.Values);

        PackageState state = Acquire(root);
        return new LoadedContentPackage(this, state.Id);
    }

    internal TextureRef GetTexture(string packageId, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var state = GetLivePackage(packageId);
        return TryFindTexture(state, name, out var texture)
            ? texture
            : throw new KeyNotFoundException(
                $"Texture '{name}' is not visible from package '{packageId}'.");
    }

    internal SpriteRef GetSprite(string packageId, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var state = GetLivePackage(packageId);
        return TryFindSprite(state, name, out var sprite)
            ? sprite
            : throw new KeyNotFoundException(
                $"Sprite '{name}' is not visible from package '{packageId}'.");
    }

    internal AnimationClipRef GetAnimation(string packageId, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        PackageState state = GetLivePackage(packageId);
        return TryFindAnimation(state, name, out AnimationClipRef animation)
            ? animation
            : throw new KeyNotFoundException(
                $"Animation '{name}' is not visible from package '{packageId}'.");
    }

    internal AudioClipRef GetAudioClip(string packageId, string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        PackageState state = GetLivePackage(packageId);
        return TryFindAudioClip(state, name, out AudioClipRef clip)
            ? clip
            : throw new KeyNotFoundException(
                $"Audio clip '{name}' is not visible from package '{packageId}'.");
    }

    internal void Release(string packageId)
    {
        if (_disposed || !_packagesById.TryGetValue(packageId, out var state))
            return;
        Release(state);
    }

    public void Dispose()
    {
        if (_disposed) return;

        var states = _packagesById.Values
            .OrderByDescending(state => state.LoadOrder)
            .ToArray();
        foreach (var state in states)
        {
            for (int i = state.AudioClips.Count - 1; i >= 0; i--)
                _audio.Remove(state.AudioClips[i]);
            for (int i = state.Animations.Count - 1; i >= 0; i--)
                _animations.Remove(state.Animations[i]);
            for (int i = state.Sprites.Count - 1; i >= 0; i--)
                _sprites.Remove(state.Sprites[i]);
        }
        foreach (var state in states)
        {
            for (int i = state.Textures.Count - 1; i >= 0; i--)
                _textures.Remove(state.Textures[i]);
        }

        _packagesById.Clear();
        _packagesByPath.Clear();
        _disposed = true;
    }

    private GraphNode ReadGraph(
        string manifestPath,
        string? expectedId,
        Dictionary<string, GraphNode> nodesByPath,
        Dictionary<string, GraphNode> nodesById,
        HashSet<string> visiting)
    {
        if (nodesByPath.TryGetValue(manifestPath, out var known))
        {
            if (!string.IsNullOrEmpty(expectedId) && !StringComparer.Ordinal.Equals(known.Manifest.Id, expectedId))
                throw DependencyIdMismatch(expectedId, known.Manifest.Id, manifestPath);
            if (visiting.Contains(manifestPath))
                throw new InvalidDataException($"Content package dependency cycle reaches '{known.Manifest.Id}'.");
            return known;
        }

        using var stream = File.OpenRead(manifestPath);
        var manifest = AssetPackageManifestParser.Parse(stream);
        if (!string.IsNullOrEmpty(expectedId) && !StringComparer.Ordinal.Equals(manifest.Id, expectedId))
            throw DependencyIdMismatch(expectedId, manifest.Id, manifestPath);

        if (_packagesById.TryGetValue(manifest.Id, out var loaded) &&
            !PathComparer.Equals(loaded.ManifestPath, manifestPath))
        {
            throw new InvalidDataException(
                $"Package id '{manifest.Id}' is already associated with '{loaded.ManifestPath}', not '{manifestPath}'.");
        }
        if (nodesById.TryGetValue(manifest.Id, out var sameId) &&
            !PathComparer.Equals(sameId.ManifestPath, manifestPath))
        {
            throw new InvalidDataException(
                $"Package id '{manifest.Id}' resolves to multiple manifests.");
        }

        var node = new GraphNode { ManifestPath = manifestPath, Manifest = manifest };
        nodesByPath.Add(manifestPath, node);
        nodesById[manifest.Id] = node;
        visiting.Add(manifestPath);
        try
        {
            foreach (var dependency in manifest.Dependencies)
            {
                string dependencyPath = ResolveUnderRoot(
                    _packagesRoot,
                    dependency.Manifest,
                    $"Dependency manifest for '{dependency.Id}'");
                node.Dependencies.Add(ReadGraph(
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

    private void ValidateGraphBeforeLoading(IEnumerable<GraphNode> nodes)
    {
        GraphNode[] graph = nodes.ToArray();
        var textureNames = new HashSet<string>(StringComparer.Ordinal);
        var spriteNames = new HashSet<string>(StringComparer.Ordinal);
        var animationNames = new HashSet<string>(StringComparer.Ordinal);
        var audioNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph)
        {
            if (_packagesByPath.ContainsKey(node.ManifestPath))
                continue;

            string packageDirectory = Path.GetDirectoryName(node.ManifestPath)
                ?? throw new InvalidDataException($"Manifest '{node.ManifestPath}' has no package directory.");

            foreach (var definition in node.Manifest.Textures)
            {
                string path = ResolveUnderRoot(packageDirectory, definition.Path, "Texture");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Texture asset '{definition.Path}' does not exist.", path);
                if (!textureNames.Add(definition.Name) ||
                    _textures.TryGetMetadata(new TextureRef(definition.Name), out _))
                {
                    throw new InvalidDataException(
                        $"Texture name '{definition.Name}' conflicts with an existing package or resource.");
                }
            }

            foreach (var definition in node.Manifest.Sprites)
            {
                if (!spriteNames.Add(definition.Name) ||
                    _sprites.TryGetMetadata(new SpriteRef(definition.Name), out _))
                {
                    throw new InvalidDataException(
                        $"Sprite name '{definition.Name}' conflicts with an existing package or resource.");
                }
            }

            foreach (AnimationAssetDefinition definition in node.Manifest.Animations)
            {
                if (!animationNames.Add(definition.Name) ||
                    _animations.TryGet(new AnimationClipRef(definition.Name), out _))
                {
                    throw new InvalidDataException(
                        $"Animation name '{definition.Name}' conflicts with an existing package or resource.");
                }
            }

            foreach (AudioAssetDefinition definition in node.Manifest.AudioClips)
            {
                if (definition.Streaming)
                    throw new InvalidDataException(
                        $"Audio clip '{definition.Name}' requests streaming, which is not supported by the short-SFX slice.");
                string path = ResolveUnderRoot(packageDirectory, definition.Path, "Audio clip");
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Audio asset '{definition.Path}' does not exist.", path);
                if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(path), ".wav"))
                    throw new InvalidDataException($"Audio clip '{definition.Name}' must use a .wav asset in v1.");
                if (!audioNames.Add(definition.Name) ||
                    _audio.TryGet(new AudioClipRef(definition.Name), out _))
                {
                    throw new InvalidDataException(
                        $"Audio clip name '{definition.Name}' conflicts with an existing package or resource.");
                }
            }
        }

        foreach (var node in graph)
        {
            ValidateSpriteTextureReferences(node);
            ValidateAnimationSpriteReferences(node);
        }
    }

    private static void ValidateAnimationSpriteReferences(GraphNode node)
    {
        var visibleSprites = new HashSet<string>(StringComparer.Ordinal);
        CollectVisibleSpriteNames(node, visibleSprites, new HashSet<string>(PathComparer));
        foreach (AnimationAssetDefinition animation in node.Manifest.Animations)
        {
            if (!visibleSprites.Contains(animation.SpriteName))
            {
                throw new InvalidDataException(
                    $"Animation '{animation.Name}' references Sprite '{animation.SpriteName}' " +
                    "outside its package dependency closure.");
            }
        }
    }

    private static void CollectVisibleSpriteNames(
        GraphNode node,
        HashSet<string> names,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return;
        foreach (SpriteAssetDefinition sprite in node.Manifest.Sprites)
            names.Add(sprite.Name);
        foreach (GraphNode dependency in node.Dependencies)
            CollectVisibleSpriteNames(dependency, names, visited);
    }

    private static void ValidateSpriteTextureReferences(GraphNode node)
    {
        var visibleTextures = new HashSet<string>(StringComparer.Ordinal);
        CollectVisibleTextureNames(node, visibleTextures, new HashSet<string>(PathComparer));

        foreach (var sprite in node.Manifest.Sprites)
        {
            if (sprite.Layout is SpriteAssetLayout.Single or SpriteAssetLayout.Grid)
            {
                RequireVisibleTexture(sprite.TextureName!, sprite.Name, visibleTextures);
                continue;
            }

            foreach (var frame in sprite.Frames)
            {
                string textureName = frame.TextureName ?? sprite.TextureName!;
                RequireVisibleTexture(textureName, sprite.Name, visibleTextures);
            }
        }
    }

    private static void CollectVisibleTextureNames(
        GraphNode node,
        HashSet<string> names,
        HashSet<string> visited)
    {
        if (!visited.Add(node.ManifestPath)) return;
        foreach (var texture in node.Manifest.Textures)
            names.Add(texture.Name);
        foreach (var dependency in node.Dependencies)
            CollectVisibleTextureNames(dependency, names, visited);
    }

    private static void RequireVisibleTexture(
        string textureName,
        string spriteName,
        HashSet<string> visibleTextures)
    {
        if (!visibleTextures.Contains(textureName))
        {
            throw new InvalidDataException(
                $"Sprite '{spriteName}' references Texture '{textureName}' outside its package dependency closure.");
        }
    }

    private PackageState Acquire(GraphNode node)
    {
        if (_packagesByPath.TryGetValue(node.ManifestPath, out var existing))
        {
            checked { existing.ReferenceCount++; }
            return existing;
        }

        var dependencies = new List<PackageState>(node.Dependencies.Count);
        var textures = new List<TextureRef>();
        var sprites = new List<SpriteRef>();
        var animations = new List<AnimationClipRef>();
        var audioClips = new List<AudioClipRef>();
        try
        {
            foreach (var dependency in node.Dependencies)
                dependencies.Add(Acquire(dependency));

            if (node.Manifest.Textures.Count > 0)
            {
                string packageDirectory = Path.GetDirectoryName(node.ManifestPath)!;
                var textureManifest = new TextureAssetManifest(node.Manifest.Textures);
                textures.AddRange(TextureManifestLoader.LoadInto(
                    _textures,
                    textureManifest,
                    packageDirectory));
            }

            foreach (var definition in node.Manifest.Sprites)
                sprites.Add(RegisterSprite(definition, textures, dependencies));

            foreach (AnimationAssetDefinition definition in node.Manifest.Animations)
                animations.Add(RegisterAnimation(definition, sprites, dependencies));

            if (node.Manifest.AudioClips.Count > 0)
            {
                string packageDirectory = Path.GetDirectoryName(node.ManifestPath)!;
                foreach (AudioAssetDefinition definition in node.Manifest.AudioClips)
                {
                    string path = ResolveUnderRoot(packageDirectory, definition.Path, "Audio clip");
                    DecodedAudioClip decoded = WaveAudioDecoder.DecodeFile(path);
                    audioClips.Add(_audio.RegisterDecoded(definition.Name, path, decoded));
                }
            }

            var state = new PackageState
            {
                Id = node.Manifest.Id,
                ManifestPath = node.ManifestPath,
                Textures = textures.ToArray(),
                Sprites = sprites.ToArray(),
                Animations = animations.ToArray(),
                AudioClips = audioClips.ToArray(),
                Dependencies = dependencies.ToArray(),
                ReferenceCount = 1,
                LoadOrder = ++_nextLoadOrder
            };
            _packagesById.Add(state.Id, state);
            _packagesByPath.Add(state.ManifestPath, state);
            _revisionGeneration++;
            return state;
        }
        catch
        {
            for (int i = audioClips.Count - 1; i >= 0; i--)
                _audio.Remove(audioClips[i]);
            for (int i = animations.Count - 1; i >= 0; i--)
                _animations.Remove(animations[i]);
            for (int i = sprites.Count - 1; i >= 0; i--)
                _sprites.Remove(sprites[i]);
            for (int i = textures.Count - 1; i >= 0; i--)
                _textures.Remove(textures[i]);
            for (int i = dependencies.Count - 1; i >= 0; i--)
                Release(dependencies[i]);
            throw;
        }
    }

    private AnimationClipRef RegisterAnimation(
        AnimationAssetDefinition definition,
        IReadOnlyList<SpriteRef> ownSprites,
        IReadOnlyList<PackageState> dependencies)
    {
        SpriteRef sprite = FindAllowedSprite(definition.SpriteName, ownSprites, dependencies);
        if (!_sprites.TryGetMetadata(sprite, out SpriteMetadata metadata))
            throw new InvalidDataException($"Animation '{definition.Name}' Sprite '{sprite}' is unavailable.");
        for (int i = 0; i < definition.Frames.Count; i++)
        {
            if (definition.Frames[i] >= metadata.FrameCount)
            {
                throw new InvalidDataException(
                    $"Animation '{definition.Name}' frame {i} references sub-image " +
                    $"{definition.Frames[i]}, but Sprite '{sprite}' has {metadata.FrameCount} frames.");
            }
        }

        AnimationFrameMarker[] markers = definition.Markers
            .Select(marker => new AnimationFrameMarker(
                marker.Frame,
                new AnimationEventRef(marker.Event)))
            .ToArray();
        try
        {
            return _animations.Register(
                definition.Name,
                sprite,
                definition.Frames.ToArray(),
                definition.FramesPerSecond,
                definition.LoopMode,
                markers);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Animation '{definition.Name}' is invalid.", ex);
        }
    }

    private static SpriteRef FindAllowedSprite(
        string name,
        IReadOnlyList<SpriteRef> ownSprites,
        IReadOnlyList<PackageState> dependencies)
    {
        foreach (SpriteRef sprite in ownSprites)
        {
            if (StringComparer.Ordinal.Equals(sprite.Name, name))
                return sprite;
        }
        foreach (PackageState dependency in dependencies)
        {
            if (TryFindSprite(dependency, name, out SpriteRef sprite))
                return sprite;
        }
        throw new InvalidDataException($"Animation references unavailable Sprite '{name}'.");
    }

    private SpriteRef RegisterSprite(
        SpriteAssetDefinition definition,
        IReadOnlyList<TextureRef> ownTextures,
        IReadOnlyList<PackageState> dependencies)
    {
        try
        {
            SpriteFrameSource[] frames = definition.Layout switch
            {
                SpriteAssetLayout.Single => BuildSingleFrames(definition, ownTextures, dependencies),
                SpriteAssetLayout.Grid => BuildGridFrames(definition, ownTextures, dependencies),
                SpriteAssetLayout.Frames => BuildExplicitFrames(definition, ownTextures, dependencies),
                _ => throw new InvalidDataException($"Unsupported Sprite layout '{definition.Layout}'.")
            };

            var logicalSize = definition.LogicalSize ??
                new Vector2(frames[0].SourceRect.Width, frames[0].SourceRect.Height);
            return _sprites.RegisterPixelFrames(
                definition.Name,
                logicalSize,
                definition.Origin,
                frames,
                definition.FramesPerSecond);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Sprite '{definition.Name}' is invalid.", ex);
        }
    }

    private SpriteFrameSource[] BuildSingleFrames(
        SpriteAssetDefinition definition,
        IReadOnlyList<TextureRef> ownTextures,
        IReadOnlyList<PackageState> dependencies)
    {
        TextureRef texture = FindAllowedTexture(definition.TextureName!, ownTextures, dependencies);
        PixelRectI source = definition.SourceRect ?? FullTextureRect(texture);
        return [new SpriteFrameSource(texture, source)];
    }

    private SpriteFrameSource[] BuildGridFrames(
        SpriteAssetDefinition definition,
        IReadOnlyList<TextureRef> ownTextures,
        IReadOnlyList<PackageState> dependencies)
    {
        TextureRef texture = FindAllowedTexture(definition.TextureName!, ownTextures, dependencies);
        var metadata = GetTextureMetadata(texture);
        PixelSizeI size = definition.FrameSize!.Value;
        int count = definition.FrameCount!.Value;
        int columns = metadata.Width / size.Width;
        int rows = metadata.Height / size.Height;
        if (columns <= 0 || rows <= 0 || count > checked(columns * rows))
            throw new InvalidDataException($"Grid Sprite '{definition.Name}' exceeds Texture '{texture.Name}'.");

        var frames = new SpriteFrameSource[count];
        for (int i = 0; i < count; i++)
        {
            frames[i] = new SpriteFrameSource(
                texture,
                new PixelRectI(
                    (i % columns) * size.Width,
                    (i / columns) * size.Height,
                    size.Width,
                    size.Height));
        }
        return frames;
    }

    private SpriteFrameSource[] BuildExplicitFrames(
        SpriteAssetDefinition definition,
        IReadOnlyList<TextureRef> ownTextures,
        IReadOnlyList<PackageState> dependencies)
    {
        var frames = new SpriteFrameSource[definition.Frames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            var frame = definition.Frames[i];
            string textureName = frame.TextureName ?? definition.TextureName!;
            TextureRef texture = FindAllowedTexture(textureName, ownTextures, dependencies);
            frames[i] = new SpriteFrameSource(
                texture,
                frame.SourceRect ?? FullTextureRect(texture));
        }
        return frames;
    }

    private TextureRef FindAllowedTexture(
        string name,
        IReadOnlyList<TextureRef> ownTextures,
        IReadOnlyList<PackageState> dependencies)
    {
        foreach (var texture in ownTextures)
        {
            if (StringComparer.Ordinal.Equals(texture.Name, name))
                return texture;
        }
        foreach (var dependency in dependencies)
        {
            if (TryFindTexture(dependency, name, out var texture))
                return texture;
        }
        throw new InvalidDataException(
            $"Texture '{name}' is not declared by this package or a transitive dependency.");
    }

    private PixelRectI FullTextureRect(TextureRef texture)
    {
        var metadata = GetTextureMetadata(texture);
        return new PixelRectI(0, 0, metadata.Width, metadata.Height);
    }

    private TextureMetadata GetTextureMetadata(TextureRef texture) =>
        _textures.TryGetMetadata(texture, out var metadata)
            ? metadata
            : throw new InvalidDataException($"Texture '{texture.Name}' is unavailable.");

    private PackageState GetLivePackage(string packageId) =>
        _packagesById.TryGetValue(packageId, out var state)
            ? state
            : throw new ObjectDisposedException(
                nameof(LoadedContentPackage),
                $"Content package '{packageId}' is no longer loaded.");

    private static bool TryFindTexture(PackageState state, string name, out TextureRef texture)
    {
        foreach (var candidate in state.Textures)
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, name))
            {
                texture = candidate;
                return true;
            }
        }
        foreach (var dependency in state.Dependencies)
        {
            if (TryFindTexture(dependency, name, out texture))
                return true;
        }
        texture = default;
        return false;
    }

    private static bool TryFindSprite(PackageState state, string name, out SpriteRef sprite)
    {
        foreach (var candidate in state.Sprites)
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, name))
            {
                sprite = candidate;
                return true;
            }
        }
        foreach (var dependency in state.Dependencies)
        {
            if (TryFindSprite(dependency, name, out sprite))
                return true;
        }
        sprite = default;
        return false;
    }

    private static bool TryFindAnimation(
        PackageState state,
        string name,
        out AnimationClipRef animation)
    {
        foreach (AnimationClipRef candidate in state.Animations)
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, name))
            {
                animation = candidate;
                return true;
            }
        }
        foreach (PackageState dependency in state.Dependencies)
        {
            if (TryFindAnimation(dependency, name, out animation))
                return true;
        }
        animation = default;
        return false;
    }

    private static bool TryFindAudioClip(
        PackageState state,
        string name,
        out AudioClipRef clip)
    {
        foreach (AudioClipRef candidate in state.AudioClips)
        {
            if (StringComparer.Ordinal.Equals(candidate.Name, name))
            {
                clip = candidate;
                return true;
            }
        }
        foreach (PackageState dependency in state.Dependencies)
        {
            if (TryFindAudioClip(dependency, name, out clip))
                return true;
        }
        clip = default;
        return false;
    }

    private void Release(PackageState state)
    {
        if (state.ReferenceCount <= 0)
            return;
        state.ReferenceCount--;
        if (state.ReferenceCount != 0)
            return;

        for (int i = state.AudioClips.Count - 1; i >= 0; i--)
            _audio.Remove(state.AudioClips[i]);
        for (int i = state.Animations.Count - 1; i >= 0; i--)
            _animations.Remove(state.Animations[i]);
        for (int i = state.Sprites.Count - 1; i >= 0; i--)
            _sprites.Remove(state.Sprites[i]);
        for (int i = state.Textures.Count - 1; i >= 0; i--)
            _textures.Remove(state.Textures[i]);

        _packagesById.Remove(state.Id);
        _packagesByPath.Remove(state.ManifestPath);
        _revisionGeneration++;
        for (int i = state.Dependencies.Count - 1; i >= 0; i--)
            Release(state.Dependencies[i]);
    }

    private static string ResolveUnderRoot(string root, string relativePath, string kind)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"{kind} paths must be non-empty and relative.");

        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"{kind} path '{relativePath}' escapes its configured root.");
        }
        return fullPath;
    }

    private static InvalidDataException DependencyIdMismatch(
        string expected,
        string actual,
        string path) => new(
            $"Dependency expected package id '{expected}', but '{path}' declares '{actual}'.");

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
