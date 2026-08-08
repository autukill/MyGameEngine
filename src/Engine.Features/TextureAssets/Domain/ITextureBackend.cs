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
}
