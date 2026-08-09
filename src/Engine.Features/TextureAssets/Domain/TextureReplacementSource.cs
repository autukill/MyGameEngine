namespace GameEngine.Features.TextureAssets.Domain;

/// <summary>已在 CPU 侧解码、等待窗口线程上传的 RGBA8 Texture。</summary>
public sealed record TextureReplacementSource(
    string Name,
    int Width,
    int Height,
    byte[] RgbaPixels,
    TextureSampler Sampler);
