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
        MultilineLayouter = new TextLayouter(Fonts);
        Atlas = new DynamicGlyphAtlas(
            Fonts,
            new TextureLibraryGlyphUploader(textures),
            atlasOptions,
            atlasNamePrefix);
        Renderer = new TextRenderer(Layouter, MultilineLayouter, Atlas, textures);
    }

    public TextureLibrary Textures { get; }
    public FontLibrary Fonts { get; }
    public SingleLineTextLayouter Layouter { get; }
    public TextLayouter MultilineLayouter { get; }
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

    public PreparedTextLayout Prepare(
        FontFamily fonts,
        string text,
        float pixelSize,
        TextLayoutOptions options)
    {
        ThrowIfDisposed();
        return Renderer.Prepare(fonts, text, pixelSize, options);
    }

    public void PrepareInto(
        FontFamily fonts,
        string text,
        float pixelSize,
        TextLayoutOptions options,
        TextLayoutBuffer layout,
        PreparedTextLayoutBuffer prepared)
    {
        ThrowIfDisposed();
        Renderer.PrepareInto(fonts, text, pixelSize, options, layout, prepared);
    }

    public void Draw(
        ISpriteBatch batch,
        FontFamily fonts,
        string text,
        Vector2 position,
        float pixelSize,
        Vector4? color = null,
        TextLayoutOptions layout = default)
    {
        ThrowIfDisposed();
        batch.DrawText(Renderer, fonts, text, position, pixelSize, color, layout);
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

    public void Draw(
        ISpriteBatch batch,
        PreparedTextLayoutBuffer prepared,
        Vector2 position,
        Vector4? color = null)
    {
        ThrowIfDisposed();
        batch.DrawText(Renderer, prepared, position, color);
    }

    public TextRuntimeDiagnostics CaptureDiagnostics()
    {
        ThrowIfDisposed();
        return new TextRuntimeDiagnostics(
            MultilineLayouter.CaptureDiagnostics(),
            Atlas.CaptureDiagnostics());
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

public readonly record struct TextRuntimeDiagnostics(
    TextLayouterDiagnostics Layout,
    GlyphAtlasDiagnostics Atlas);
