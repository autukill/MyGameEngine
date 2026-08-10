namespace GameEngine.Tools.AssetCompiler;

using System.Text;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;

public sealed record ContentReferenceGenerationRequest(
    string CompiledPackagesRoot,
    string RootRelativeManifestPath,
    string OutputFile,
    string Namespace,
    string RootClassName = "GameAssets");

public sealed record ContentReferenceGenerationResult(
    string OutputFile,
    bool Changed,
    int PackageCount,
    int TextureCount,
    int SpriteCount,
    int AnimationCount,
    int AnimationEventCount);

/// <summary>
/// Generates stable logical C# references from the compiled runtime manifest graph. Atlas page
/// textures remain private implementation details and source textures removed by Atlas packing
/// cannot leak into the generated API.
/// </summary>
public sealed class ContentReferenceCodeGenerator
{
    private const string InternalAtlasTexturePrefix = "__atlas.";
    private sealed class GraphNode
    {
        public required string ManifestPath { get; init; }
        public required string RelativeManifestPath { get; init; }
        public required AssetPackageManifest Manifest { get; init; }
    }

    private sealed record GeneratedMember(string Identifier, string LogicalName, string? Manifest);

    public ContentReferenceGenerationResult Generate(ContentReferenceGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CompiledPackagesRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RootRelativeManifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFile);
        GeneratedCodeUtilities.ValidateNamespace(request.Namespace);
        GeneratedCodeUtilities.ValidateIdentifier(request.RootClassName, "Generated root class name");

        string packagesRoot = Path.GetFullPath(request.CompiledPackagesRoot);
        if (!Directory.Exists(packagesRoot))
            throw new DirectoryNotFoundException(
                $"Compiled packages root '{packagesRoot}' does not exist.");

        string rootManifest = ResolveUnderRoot(
            packagesRoot,
            request.RootRelativeManifestPath,
            "Root manifest");
        var nodesByPath = new Dictionary<string, GraphNode>(PathComparer);
        var nodesById = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        ReadGraph(packagesRoot, rootManifest, null, nodesByPath, nodesById, []);

        GraphNode root = nodesByPath[rootManifest];
        GeneratedMember[] packages = CreateMembers(
            "Package",
            nodesById.Values
                .Where(node => !ReferenceEquals(node, root))
                .Select(node => (node.Manifest.Id, (string?)node.RelativeManifestPath)));
        GeneratedMember[] textures = CreateMembers(
            "Texture",
            nodesById.Values
                .SelectMany(node => node.Manifest.Textures)
                .Where(texture => !texture.Name.StartsWith(
                    InternalAtlasTexturePrefix,
                    StringComparison.Ordinal))
                .Select(texture => (texture.Name, (string?)null)));
        GeneratedMember[] sprites = CreateMembers(
            "Sprite",
            nodesById.Values
                .SelectMany(node => node.Manifest.Sprites)
                .Select(sprite => (sprite.Name, (string?)null)));
        GeneratedMember[] animations = CreateMembers(
            "Animation",
            nodesById.Values
                .SelectMany(node => node.Manifest.Animations)
                .Select(animation => (animation.Name, (string?)null)));
        GeneratedMember[] animationEvents = CreateMembers(
            "Animation event",
            nodesById.Values
                .SelectMany(node => node.Manifest.Animations)
                .SelectMany(animation => animation.Markers)
                .Select(marker => (marker.Event, (string?)null))
                .Distinct());

        string outputFile = Path.GetFullPath(request.OutputFile);
        ValidateOutputFile(packagesRoot, outputFile);
        string source = RenderSource(
            request.Namespace,
            request.RootClassName,
            root,
            packages,
            textures,
            sprites,
            animations,
            animationEvents);
        bool changed = GeneratedCodeUtilities.WriteIfChanged(outputFile, source);
        return new ContentReferenceGenerationResult(
            outputFile,
            changed,
            nodesById.Count,
            textures.Length,
            sprites.Length,
            animations.Length,
            animationEvents.Length);
    }

    private static GraphNode ReadGraph(
        string packagesRoot,
        string manifestPath,
        string? expectedId,
        Dictionary<string, GraphNode> nodesByPath,
        Dictionary<string, GraphNode> nodesById,
        HashSet<string> visiting)
    {
        if (!visiting.Add(manifestPath))
            throw new InvalidDataException(
                $"Content package dependency cycle reaches '{manifestPath}'.");
        try
        {
            if (nodesByPath.TryGetValue(manifestPath, out var known))
            {
                ValidateExpectedId(expectedId, known.Manifest.Id, manifestPath);
                return known;
            }

            using var stream = File.OpenRead(manifestPath);
            AssetPackageManifest manifest = AssetPackageManifestParser.Parse(stream);
            ValidateExpectedId(expectedId, manifest.Id, manifestPath);
            if (nodesById.TryGetValue(manifest.Id, out var sameId) &&
                !PathComparer.Equals(sameId.ManifestPath, manifestPath))
            {
                throw new InvalidDataException(
                    $"Package id '{manifest.Id}' resolves to multiple compiled manifests.");
            }

            var node = new GraphNode
            {
                ManifestPath = manifestPath,
                RelativeManifestPath = NormalizeRelativePath(
                    Path.GetRelativePath(packagesRoot, manifestPath)),
                Manifest = manifest
            };
            nodesByPath.Add(manifestPath, node);
            nodesById.Add(manifest.Id, node);
            foreach (AssetPackageDependency dependency in manifest.Dependencies)
            {
                string dependencyPath = ResolveUnderRoot(
                    packagesRoot,
                    dependency.Manifest,
                    "Dependency manifest");
                ReadGraph(
                    packagesRoot,
                    dependencyPath,
                    dependency.Id,
                    nodesByPath,
                    nodesById,
                    visiting);
            }
            return node;
        }
        finally
        {
            visiting.Remove(manifestPath);
        }
    }

    private static GeneratedMember[] CreateMembers(
        string kind,
        IEnumerable<(string LogicalName, string? Manifest)> values)
    {
        var logicalNames = new HashSet<string>(StringComparer.Ordinal);
        var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new List<GeneratedMember>();
        foreach ((string logicalName, string? manifest) in values
                     .OrderBy(value => value.LogicalName, StringComparer.Ordinal))
        {
            if (!logicalNames.Add(logicalName))
                throw new InvalidDataException(
                    $"{kind} '{logicalName}' appears in multiple compiled packages.");
            string identifier = GeneratedCodeUtilities.ToIdentifier(logicalName, kind);
            if (identifiers.TryGetValue(identifier, out string? existing))
            {
                throw new InvalidDataException(
                    $"{kind} names '{existing}' and '{logicalName}' both map to generated " +
                    $"identifier '{identifier}'. Rename one asset to make the C# API unambiguous.");
            }
            identifiers.Add(identifier, logicalName);
            result.Add(new GeneratedMember(identifier, logicalName, manifest));
        }
        return result.ToArray();
    }

    private static string RenderSource(
        string targetNamespace,
        string rootClassName,
        GraphNode root,
        IReadOnlyList<GeneratedMember> packages,
        IReadOnlyList<GeneratedMember> textures,
        IReadOnlyList<GeneratedMember> sprites,
        IReadOnlyList<GeneratedMember> animations,
        IReadOnlyList<GeneratedMember> animationEvents)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.Append("namespace ").Append(targetNamespace).AppendLine(";");
        source.AppendLine();
        source.Append("public static class ").Append(rootClassName).AppendLine();
        source.AppendLine("{");
        source.AppendLine("    public static class Packages");
        source.AppendLine("    {");
        AppendPackageMember(
            source,
            "Root",
            root.Manifest.Id,
            root.RelativeManifestPath);
        foreach (GeneratedMember package in packages)
            AppendPackageMember(source, package.Identifier, package.LogicalName, package.Manifest!);
        source.AppendLine("    }");
        source.AppendLine();
        AppendReferenceContainer(
            source,
            "Textures",
            "global::GameEngine.Core.Domain.ValueObjects.TextureRef",
            textures);
        source.AppendLine();
        AppendReferenceContainer(
            source,
            "Sprites",
            "global::GameEngine.Core.Domain.ValueObjects.SpriteRef",
            sprites);
        source.AppendLine();
        AppendReferenceContainer(
            source,
            "Animations",
            "global::GameEngine.Features.Animation.AnimationClipRef",
            animations);
        source.AppendLine();
        AppendReferenceContainer(
            source,
            "AnimationEvents",
            "global::GameEngine.Features.Animation.AnimationEventRef",
            animationEvents);
        source.AppendLine("}");
        return source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendPackageMember(
        StringBuilder source,
        string identifier,
        string id,
        string manifest)
    {
        source.Append("        public static readonly global::GameEngine.Features.ContentAssets.Domain.ContentPackageRef ")
            .Append(identifier)
            .Append(" = new(")
            .Append(GeneratedCodeUtilities.Literal(id))
            .Append(", ")
            .Append(GeneratedCodeUtilities.Literal(manifest))
            .AppendLine(");");
    }

    private static void AppendReferenceContainer(
        StringBuilder source,
        string containerName,
        string referenceType,
        IReadOnlyList<GeneratedMember> members)
    {
        source.Append("    public static class ").Append(containerName).AppendLine();
        source.AppendLine("    {");
        foreach (GeneratedMember member in members)
        {
            source.Append("        public static readonly ")
                .Append(referenceType)
                .Append(' ')
                .Append(member.Identifier)
                .Append(" = new(")
                .Append(GeneratedCodeUtilities.Literal(member.LogicalName))
                .AppendLine(");");
        }
        source.AppendLine("    }");
    }

    private static void ValidateOutputFile(string packagesRoot, string outputFile)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputFile), ".cs"))
            throw new InvalidDataException("Generated content reference output must be a .cs file.");

        string relative = Path.GetRelativePath(packagesRoot, outputFile);
        bool insideCompiledPackages = relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
        if (insideCompiledPackages)
        {
            throw new InvalidDataException(
                "Generated content references cannot be written inside the compiled packages root.");
        }
    }

    private static void ValidateExpectedId(string? expected, string actual, string manifestPath)
    {
        if (expected is not null && !StringComparer.Ordinal.Equals(expected, actual))
        {
            throw new InvalidDataException(
                $"Dependency expected package '{expected}', but manifest '{manifestPath}' " +
                $"contains '{actual}'.");
        }
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
            throw new InvalidDataException(
                $"{kind} path '{relativePath}' escapes the compiled packages root.");
        }
        return full;
    }

    private static string NormalizeRelativePath(string value) => value.Replace('\\', '/');

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
