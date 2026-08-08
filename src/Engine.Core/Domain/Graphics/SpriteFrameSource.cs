namespace GameEngine.Core.Domain.Graphics;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>An integer pixel rectangle in top-left image coordinates.</summary>
public readonly record struct PixelRectI(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

/// <summary>A source texture and pixel rectangle for one logical Sprite frame.</summary>
public readonly record struct SpriteFrameSource(
    TextureRef Texture,
    PixelRectI SourceRect);
