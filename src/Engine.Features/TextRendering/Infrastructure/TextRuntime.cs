namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.TextRendering.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>
/// Developer-facing owner for fonts, layout, glyph pages and drawing. Dispose it before TextureLibrary.
/// </summary>
public sealed class TextRuntime : IDisposable
{
    private bool _disposed;

    public TextRuntime(
        TextureLibrary textures,
        GlyphAtlasOptions? atlasOptions = null,
        string atlasNamePrefix = "__text.glyph-atlas")
    {
        Textures = textures ?? throw new ArgumentNullException(nameof(textures));
        Fonts = new FontLibrary();
        Layouter = new SingleLineTextLayouter(Fonts);
        Atlas = new DynamicGlyphAtlas(
            Fonts,
            new TextureLibraryGlyphUploader(textures),
            atlasOptions,
            atlasNamePrefix);
        Renderer = new TextRenderer(Layouter, Atlas, textures);
    }

    public TextureLibrary Textures { get; }
    public FontLibrary Fonts { get; }
    public SingleLineTextLayouter Layouter { get; }
    public DynamicGlyphAtlas Atlas { get; }
    public TextRenderer Renderer { get; }

    public FontRef LoadFont(string name, string path, int faceIndex = 0)
    {
        ThrowIfDisposed();
        SkiaGlyphRasterizer rasterizer = SkiaGlyphRasterizer.Load(path, faceIndex);
        try
        {
            return Fonts.Register(name, rasterizer.Metadata, rasterizer);
        }
        catch
        {
            rasterizer.Dispose();
            throw;
        }
    }

    public FontRef LoadFont(string name, Stream stream, int faceIndex = 0)
    {
        ThrowIfDisposed();
        SkiaGlyphRasterizer rasterizer = SkiaGlyphRasterizer.Load(stream, faceIndex);
        try
        {
            return Fonts.Register(name, rasterizer.Metadata, rasterizer);
        }
        catch
        {
            rasterizer.Dispose();
            throw;
        }
    }

    public FontRef LoadSystemFont(string name, string familyName)
    {
        ThrowIfDisposed();
        SkiaGlyphRasterizer rasterizer = SkiaGlyphRasterizer.FromFamilyName(familyName);
        try
        {
            return Fonts.Register(name, rasterizer.Metadata, rasterizer);
        }
        catch
        {
            rasterizer.Dispose();
            throw;
        }
    }

    public FontFamily CreateFamily(FontRef primary, params FontRef[] fallbacks)
    {
        ThrowIfDisposed();
        return Fonts.CreateFamily(primary, fallbacks);
    }

    public PreparedTextLayout Prepare(FontFamily fonts, string text, float pixelSize)
    {
        ThrowIfDisposed();
        return Renderer.Prepare(fonts, text, pixelSize);
    }

    public void Draw(
        ISpriteBatch batch,
        FontFamily fonts,
        string text,
        Vector2 position,
        float pixelSize,
        Vector4? color = null)
    {
        ThrowIfDisposed();
        batch.DrawText(Renderer, fonts, text, position, pixelSize, color);
    }

    public void Draw(
        ISpriteBatch batch,
        PreparedTextLayout prepared,
        Vector2 position,
        Vector4? color = null)
    {
        ThrowIfDisposed();
        batch.DrawText(Renderer, prepared, position, color);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Atlas.Dispose();
        Fonts.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
