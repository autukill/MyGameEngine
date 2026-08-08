namespace GameEngine.Features.TextureAssets.Domain;

/// <summary>Unpremultiplied RGBA8 pixels ready for upload.</summary>
public readonly record struct DecodedImage(
    int Width,
    int Height,
    byte[] RgbaPixels);
