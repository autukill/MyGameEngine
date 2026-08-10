namespace GameEngine.Features.TextRendering.Domain;

using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Logical texture upload boundary for glyph atlas pages. Implementations may delegate to TextureLibrary;
/// callers never observe a native GPU handle.
/// </summary>
public interface IGlyphTextureUploader
{
    TextureRef CreateAlphaPage(string name, int width, int height);
    void UploadAlpha(TextureRef texture, PixelRectI destination, ReadOnlySpan<byte> alphaPixels);
    void DeletePage(TextureRef texture);
}

public readonly record struct GlyphAtlasOptions(
    int PageWidth = 512,
    int PageHeight = 512,
    int Padding = 1,
    int MaxPages = 16)
{
    /// <summary>Ensures <c>new GlyphAtlasOptions()</c> uses useful values instead of zero-initialized fields.</summary>
    public GlyphAtlasOptions() : this(512, 512, 1, 16)
    {
    }
}
