namespace GameEngine.Features.ShaderAssets.Infrastructure;

using GameEngine.Features.ShaderAssets.Domain;

public sealed record LoadedShaderAssetManifest(
    string ManifestPath,
    string RootDirectory,
    ShaderAssetManifest Manifest);

public static class ShaderAssetManifestLoader
{
    public static LoadedShaderAssetManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string fullManifest = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifest))
            throw new FileNotFoundException("Shader asset manifest was not found.", fullManifest);
        string root = Path.GetDirectoryName(fullManifest) ??
            throw new InvalidDataException("Shader asset manifest has no parent directory.");
        using FileStream stream = File.OpenRead(fullManifest);
        ShaderAssetManifest manifest = ShaderAssetManifestParser.Parse(stream);
        foreach (ShaderAssetDefinition shader in manifest.Shaders)
        {
            RequireExistingFile(root, shader.VertexPath, shader.Name, "vertex");
            RequireExistingFile(root, shader.FragmentPath, shader.Name, "fragment");
        }
        return new LoadedShaderAssetManifest(fullManifest, root, manifest);
    }

    private static void RequireExistingFile(
        string root,
        string relativePath,
        string shader,
        string stage)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException(
                $"Shader '{shader}' {stage} path '{relativePath}' escapes the manifest directory.");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Shader '{shader}' {stage} source was not found.", fullPath);
    }
}
