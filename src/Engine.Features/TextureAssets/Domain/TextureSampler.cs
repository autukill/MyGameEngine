namespace GameEngine.Features.TextureAssets.Domain;

public enum TextureFilter
{
    Nearest,
    Linear
}

public enum TextureWrap
{
    ClampToEdge,
    Repeat
}

/// <summary>Immutable sampling state applied when a texture is uploaded.</summary>
public readonly record struct TextureSampler(
    TextureFilter MinFilter,
    TextureFilter MagFilter,
    TextureWrap WrapU,
    TextureWrap WrapV)
{
    public static TextureSampler PixelArt => new(
        TextureFilter.Nearest,
        TextureFilter.Nearest,
        TextureWrap.ClampToEdge,
        TextureWrap.ClampToEdge);

    public static TextureSampler Smooth => new(
        TextureFilter.Linear,
        TextureFilter.Linear,
        TextureWrap.ClampToEdge,
        TextureWrap.ClampToEdge);
}
