namespace GameEngine.Features.TextureAssets.Infrastructure;

using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using Silk.NET.OpenGL;

/// <summary>
/// Owns uploaded texture handles. All mutation and disposal must occur on the graphics-context thread.
/// </summary>
public sealed class TextureLibrary : ITextureResolver, IDisposable
{
    private sealed record Entry(uint Handle, TextureMetadata Metadata);

    private readonly ITextureBackend _backend;
    private readonly IImageDecoder _decoder;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    public TextureLibrary(GL gl, IImageDecoder? decoder = null)
        : this(new OpenGlTextureBackend(gl), decoder)
    {
    }

    public TextureLibrary(ITextureBackend backend, IImageDecoder? decoder = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _decoder = decoder ?? new SkiaImageDecoder();
    }

    public int Count => _entries.Count;

    public TextureRef Load(
        string name,
        string path,
        TextureSampler? sampler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ValidateNewName(name);
        using var stream = File.OpenRead(path);
        return Load(name, stream, sampler);
    }

    public TextureRef Load(
        string name,
        Stream stream,
        TextureSampler? sampler = null)
    {
        ValidateNewName(name);
        ArgumentNullException.ThrowIfNull(stream);
        var image = _decoder.Decode(stream);
        return RegisterRgba(name, image.Width, image.Height, image.RgbaPixels, sampler);
    }

    public TextureRef RegisterRgba(
        string name,
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels,
        TextureSampler? sampler = null)
    {
        ValidateNewName(name);
        ValidatePixels(width, height, rgbaPixels);

        uint handle = _backend.CreateTexture(
            width,
            height,
            rgbaPixels,
            sampler ?? TextureSampler.Smooth);
        if (handle == 0)
            throw new InvalidOperationException("The texture backend returned an invalid handle.");

        try
        {
            _entries.Add(name, new Entry(handle, new TextureMetadata(width, height)));
        }
        catch
        {
            _backend.DeleteTexture(handle);
            throw;
        }

        return new TextureRef(name);
    }

    public bool TryGetMetadata(TextureRef texture, out TextureMetadata metadata)
    {
        ThrowIfDisposed();
        if (!texture.IsEmpty && _entries.TryGetValue(texture.Name, out var entry))
        {
            metadata = entry.Metadata;
            return true;
        }

        metadata = default;
        return false;
    }

    public bool TryResolve(TextureRef texture, out ResolvedTexture resolved)
    {
        ThrowIfDisposed();
        if (!texture.IsEmpty && _entries.TryGetValue(texture.Name, out var entry))
        {
            resolved = new ResolvedTexture(entry.Handle, entry.Metadata);
            return true;
        }

        resolved = default;
        return false;
    }

    public bool Remove(TextureRef texture)
    {
        ThrowIfDisposed();
        if (texture.IsEmpty || !_entries.Remove(texture.Name, out var entry))
            return false;

        _backend.DeleteTexture(entry.Handle);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        DeleteAll();
    }

    public void Dispose()
    {
        if (_disposed) return;
        DeleteAll();
        _disposed = true;
    }

    private void DeleteAll()
    {
        foreach (var entry in _entries.Values)
            _backend.DeleteTexture(entry.Handle);
        _entries.Clear();
    }

    private void ValidateNewName(string name)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Texture name cannot be empty.", nameof(name));
        if (_entries.ContainsKey(name))
            throw new ArgumentException($"Texture '{name}' is already registered.", nameof(name));
    }

    private static void ValidatePixels(
        int width,
        int height,
        ReadOnlySpan<byte> rgbaPixels)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        int expectedLength = checked(width * height * 4);
        if (rgbaPixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"RGBA8 data length must be exactly {expectedLength} bytes.",
                nameof(rgbaPixels));
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
