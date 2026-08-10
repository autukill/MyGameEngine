namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Buffers;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextRendering.Domain;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>Stores glyph Alpha8 pages as white RGBA8 textures owned by TextureLibrary.</summary>
public sealed class TextureLibraryGlyphUploader(TextureLibrary textures) : IGlyphTextureUploader
{
    private readonly TextureLibrary _textures = textures ?? throw new ArgumentNullException(nameof(textures));

    public TextureRef CreateAlphaPage(string name, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return _textures.RegisterRgba(
            name,
            width,
            height,
            new byte[checked(width * height * 4)],
            TextureSampler.Smooth);
    }

    public void UploadAlpha(TextureRef texture, PixelRectI destination, ReadOnlySpan<byte> alphaPixels)
    {
        int pixelCount = checked(destination.Width * destination.Height);
        if (alphaPixels.Length != pixelCount)
            throw new ArgumentException("Glyph alpha data does not match its destination.", nameof(alphaPixels));
        int byteCount = checked(pixelCount * 4);
        byte[] rgba = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            for (int source = 0, target = 0; source < pixelCount; source++, target += 4)
            {
                rgba[target] = 255;
                rgba[target + 1] = 255;
                rgba[target + 2] = 255;
                rgba[target + 3] = alphaPixels[source];
            }
            _textures.UpdateRgba(texture, destination, rgba.AsSpan(0, byteCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rgba);
        }
    }

    public void DeletePage(TextureRef texture) => _textures.Remove(texture);
}
