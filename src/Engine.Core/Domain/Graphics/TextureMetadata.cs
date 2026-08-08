namespace GameEngine.Core.Domain.Graphics;

/// <summary>Logical texture dimensions, independent of the graphics API.</summary>
public readonly record struct TextureMetadata(int Width, int Height)
{
    public int PixelCount => checked(Width * Height);
}

/// <summary>A texture resolved for the current graphics device.</summary>
public readonly record struct ResolvedTexture(
    uint Handle,
    TextureMetadata Metadata);
