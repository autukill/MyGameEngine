namespace GameEngine.Features.TextureAssets.Domain;

/// <summary>Graphics-device boundary used by TextureLibrary and its no-window tests.</summary>
public interface ITextureBackend
{
    uint CreateTexture(
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        TextureSampler sampler);

    void DeleteTexture(uint handle);

    /// <summary>Replaces a tightly packed RGBA8 rectangle in an existing texture.</summary>
    void UpdateTextureRegion(
        uint handle,
        int x,
        int y,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels) =>
        throw new NotSupportedException("This texture backend does not support partial updates.");
}
