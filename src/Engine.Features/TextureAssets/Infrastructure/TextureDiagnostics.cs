namespace GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>逻辑 Texture 的只读显存估算；RGBA8 无 mipmap，名称可包含内部 Atlas 页。</summary>
public sealed record TextureMemoryDiagnostics(
    string Name,
    int Width,
    int Height,
    long EstimatedBytes);

public sealed class TextureLibraryDiagnostics
{
    public int TextureCount { get; }
    public long EstimatedBytes { get; }
    public IReadOnlyList<TextureMemoryDiagnostics> Textures { get; }

    internal TextureLibraryDiagnostics(IEnumerable<TextureMemoryDiagnostics> textures)
    {
        var snapshot = textures.ToArray();
        TextureCount = snapshot.Length;
        EstimatedBytes = snapshot.Sum(item => item.EstimatedBytes);
        Textures = Array.AsReadOnly(snapshot);
    }
}
