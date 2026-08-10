namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextRendering.Domain;

/// <summary>Deterministic shelf-packed cache of rasterized glyphs across logical alpha texture pages.</summary>
public sealed class DynamicGlyphAtlas : IDisposable
{
    private readonly record struct CacheKey(FontRef Font, uint GlyphIndex, int PixelSizeBits);

    private sealed class Page(TextureRef texture)
    {
        public TextureRef Texture { get; } = texture;
        public int X;
        public int Y;
        public int ShelfHeight;
    }

    private readonly FontLibrary _fonts;
    private readonly IGlyphTextureUploader _uploader;
    private readonly GlyphAtlasOptions _options;
    private readonly string _pageNamePrefix;
    private readonly Dictionary<CacheKey, GlyphAtlasEntry> _cache = new();
    private readonly List<Page> _pages = [];
    private long _cacheHits;
    private long _cacheMisses;
    private bool _disposed;

    public DynamicGlyphAtlas(
        FontLibrary fonts,
        IGlyphTextureUploader uploader,
        GlyphAtlasOptions? options = null,
        string pageNamePrefix = "__text.glyph-atlas")
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _options = options ?? new GlyphAtlasOptions();
        if (string.IsNullOrWhiteSpace(pageNamePrefix))
            throw new ArgumentException("Glyph atlas page prefix cannot be empty.", nameof(pageNamePrefix));
        _pageNamePrefix = pageNamePrefix;
        if (_options.PageWidth <= 0 || _options.PageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas page dimensions must be positive.");
        if (_options.Padding < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas padding cannot be negative.");
        if (_options.MaxPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Atlas page count must be positive.");
    }

    public int CachedGlyphCount
    {
        get { ThrowIfDisposed(); return _cache.Count; }
    }

    public int PageCount
    {
        get { ThrowIfDisposed(); return _pages.Count; }
    }

    public GlyphAtlasEntry GetOrAdd(in GlyphPlacement placement, float pixelSize)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(pixelSize) || pixelSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        var key = new CacheKey(placement.Font, placement.GlyphIndex, BitConverter.SingleToInt32Bits(pixelSize));
        if (_cache.TryGetValue(key, out GlyphAtlasEntry existing))
        {
            _cacheHits++;
            return existing;
        }
        _cacheMisses++;

        IGlyphRasterizer rasterizer = _fonts.GetRasterizer(placement.Font);
        GlyphBitmap bitmap = rasterizer.RasterizeGlyph(placement.GlyphIndex, pixelSize)
            ?? throw new InvalidOperationException("The glyph rasterizer returned null.");
        if (bitmap.Width != placement.Metrics.Width || bitmap.Height != placement.Metrics.Height)
            throw new InvalidOperationException("Rasterized glyph dimensions differ from measured dimensions.");

        GlyphAtlasEntry entry;
        if (bitmap.IsEmpty)
        {
            entry = new GlyphAtlasEntry(TextureRef.Empty, default, default, false);
        }
        else
        {
            (Page page, PixelRectI rect) = Allocate(bitmap.Width, bitmap.Height);
            _uploader.UploadAlpha(page.Texture, rect, bitmap.AlphaPixels.Span);
            entry = new GlyphAtlasEntry(
                page.Texture,
                rect,
                new Vector4(
                    rect.X / (float)_options.PageWidth,
                    rect.Y / (float)_options.PageHeight,
                    rect.Right / (float)_options.PageWidth,
                    rect.Bottom / (float)_options.PageHeight),
                true);
        }

        _cache.Add(key, entry);
        return entry;
    }

    public PreparedTextLayout Prepare(TextLayout layout)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(layout);
        var prepared = new PreparedGlyph[layout.Glyphs.Count];
        for (int i = 0; i < prepared.Length; i++)
        {
            GlyphPlacement placement = layout.Glyphs[i];
            prepared[i] = new PreparedGlyph(placement, GetOrAdd(placement, layout.PixelSize));
        }

        return new PreparedTextLayout(layout, prepared);
    }

    public void Prepare(TextLayoutBuffer layout, PreparedTextLayoutBuffer destination)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(destination);
        destination.EnsureCapacity(layout.GlyphCount);
        ReadOnlySpan<GlyphPlacement> placements = layout.Glyphs;
        for (int i = 0; i < placements.Length; i++)
        {
            GlyphPlacement placement = placements[i];
            destination.Items[i] = new PreparedGlyph(
                placement,
                GetOrAdd(placement, layout.PixelSize));
        }
        destination.Layout = layout;
        destination.LayoutRevision = layout.Revision;
        destination.GlyphCount = layout.GlyphCount;
    }

    public GlyphAtlasDiagnostics CaptureDiagnostics()
    {
        ThrowIfDisposed();
        return new GlyphAtlasDiagnostics(
            _cache.Count,
            _pages.Count,
            _cacheHits,
            _cacheMisses);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (Page page in _pages) _uploader.DeletePage(page.Texture);
        _pages.Clear();
        _cache.Clear();
        _disposed = true;
    }

    private (Page Page, PixelRectI Rect) Allocate(int width, int height)
    {
        int paddedWidth = checked(width + _options.Padding * 2);
        int paddedHeight = checked(height + _options.Padding * 2);
        if (paddedWidth > _options.PageWidth || paddedHeight > _options.PageHeight)
            throw new InvalidOperationException("Glyph does not fit in an atlas page.");

        foreach (Page page in _pages)
        {
            if (TryAllocate(page, width, height, paddedWidth, paddedHeight, out PixelRectI rect))
                return (page, rect);
        }

        if (_pages.Count >= _options.MaxPages)
            throw new InvalidOperationException("The glyph atlas page limit has been reached.");

        string name = $"{_pageNamePrefix}.{_pages.Count:D4}";
        TextureRef texture = _uploader.CreateAlphaPage(name, _options.PageWidth, _options.PageHeight);
        if (texture.IsEmpty)
            throw new InvalidOperationException("The glyph texture uploader returned an empty TextureRef.");
        var newPage = new Page(texture);
        _pages.Add(newPage);
        if (!TryAllocate(newPage, width, height, paddedWidth, paddedHeight, out PixelRectI allocated))
            throw new InvalidOperationException("A glyph failed to fit in a newly created atlas page.");
        return (newPage, allocated);
    }

    private bool TryAllocate(
        Page page,
        int width,
        int height,
        int paddedWidth,
        int paddedHeight,
        out PixelRectI rect)
    {
        if (page.X + paddedWidth > _options.PageWidth)
        {
            page.X = 0;
            page.Y += page.ShelfHeight;
            page.ShelfHeight = 0;
        }

        if (page.Y + paddedHeight > _options.PageHeight)
        {
            rect = default;
            return false;
        }

        rect = new PixelRectI(
            page.X + _options.Padding,
            page.Y + _options.Padding,
            width,
            height);
        page.X += paddedWidth;
        page.ShelfHeight = Math.Max(page.ShelfHeight, paddedHeight);
        return true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

public readonly record struct GlyphAtlasDiagnostics(
    int CachedGlyphCount,
    int PageCount,
    long CacheHits,
    long CacheMisses);
