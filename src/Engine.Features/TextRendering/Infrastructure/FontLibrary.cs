namespace GameEngine.Features.TextRendering.Infrastructure;

using System.Text;
using GameEngine.Features.TextRendering.Domain;

/// <summary>Owns logical font registrations and optionally the injected rasterizer resources.</summary>
public sealed class FontLibrary : IDisposable
{
    private sealed record Entry(
        FontMetadata Metadata,
        IGlyphRasterizer Rasterizer,
        FontResourceOwnership Ownership);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<IGlyphRasterizer> _rasterizers = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public int Count
    {
        get { ThrowIfDisposed(); return _entries.Count; }
    }

    public FontRef Register(
        string name,
        FontMetadata metadata,
        IGlyphRasterizer rasterizer,
        FontResourceOwnership ownership = FontResourceOwnership.Owned)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Font name cannot be empty.", nameof(name));
        ValidateMetadata(metadata);
        ArgumentNullException.ThrowIfNull(rasterizer);
        if (_entries.ContainsKey(name))
            throw new ArgumentException($"Font '{name}' is already registered.", nameof(name));
        if (!_rasterizers.Add(rasterizer))
            throw new ArgumentException("A glyph rasterizer instance can only back one font registration.", nameof(rasterizer));

        _entries.Add(name, new Entry(metadata, rasterizer, ownership));
        return new FontRef(name);
    }

    public bool TryGetMetadata(FontRef font, out FontMetadata metadata)
    {
        ThrowIfDisposed();
        if (!font.IsEmpty && _entries.TryGetValue(font.Name, out Entry? entry))
        {
            metadata = entry.Metadata;
            return true;
        }

        metadata = default;
        return false;
    }

    public FontFamily CreateFamily(FontRef primary, params FontRef[] fallbacks)
    {
        ThrowIfDisposed();
        if (fallbacks is null) throw new ArgumentNullException(nameof(fallbacks));
        var fonts = new FontRef[fallbacks.Length + 1];
        fonts[0] = primary;
        fallbacks.CopyTo(fonts, 1);

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (FontRef font in fonts)
        {
            if (font.IsEmpty || !_entries.ContainsKey(font.Name))
                throw new ArgumentException($"Font '{font}' is not registered.", nameof(fallbacks));
            if (!unique.Add(font.Name))
                throw new ArgumentException($"Font '{font}' appears more than once in the fallback chain.", nameof(fallbacks));
        }

        return new FontFamily(fonts);
    }

    public bool Remove(FontRef font)
    {
        ThrowIfDisposed();
        if (font.IsEmpty || !_entries.Remove(font.Name, out Entry? entry)) return false;
        _rasterizers.Remove(entry.Rasterizer);
        DisposeOwned(entry);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (Entry entry in _entries.Values) DisposeOwned(entry);
        _entries.Clear();
        _rasterizers.Clear();
        _disposed = true;
    }

    internal FontMetadata GetMetadata(FontRef font) => GetEntry(font).Metadata;

    internal IGlyphRasterizer GetRasterizer(FontRef font) => GetEntry(font).Rasterizer;

    internal (FontRef Font, uint GlyphIndex) ResolveGlyph(FontFamily family, Rune rune)
    {
        return ResolveGlyph(family, rune, out _);
    }

    internal (FontRef Font, uint GlyphIndex) ResolveGlyph(
        FontFamily family,
        Rune rune,
        out bool missing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(family);
        ReadOnlySpan<FontRef> candidates = family.FontSpan;
        for (int i = 0; i < candidates.Length; i++)
        {
            FontRef font = candidates[i];
            Entry entry = GetEntry(font);
            if (entry.Rasterizer.TryGetGlyphIndex(rune, out uint glyphIndex))
            {
                missing = false;
                return (font, glyphIndex);
            }
        }

        FontRef primary = family.Primary;
        missing = true;
        return (primary, GetEntry(primary).Rasterizer.MissingGlyphIndex);
    }

    private Entry GetEntry(FontRef font)
    {
        ThrowIfDisposed();
        if (font.IsEmpty || !_entries.TryGetValue(font.Name, out Entry? entry))
            throw new InvalidOperationException($"Font '{font}' is not registered.");
        return entry;
    }

    private static void ValidateMetadata(FontMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.FamilyName))
            throw new ArgumentException("Font family name cannot be empty.", nameof(metadata));
        if (metadata.UnitsPerEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(metadata), "UnitsPerEm must be positive.");
        if (!float.IsFinite(metadata.AscentEm) || metadata.AscentEm <= 0)
            throw new ArgumentOutOfRangeException(nameof(metadata), "AscentEm must be finite and positive.");
        if (!float.IsFinite(metadata.DescentEm) || metadata.DescentEm < 0)
            throw new ArgumentOutOfRangeException(nameof(metadata), "DescentEm must be finite and non-negative.");
        if (!float.IsFinite(metadata.LineGapEm) || metadata.LineGapEm < 0)
            throw new ArgumentOutOfRangeException(nameof(metadata), "LineGapEm must be finite and non-negative.");
    }

    private static void DisposeOwned(Entry entry)
    {
        if (entry.Ownership == FontResourceOwnership.Owned && entry.Rasterizer is IDisposable disposable)
            disposable.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
