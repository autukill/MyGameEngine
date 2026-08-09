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
    internal sealed record Entry(uint Handle, TextureMetadata Metadata);

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

    /// <summary>上传一组暂不可见的 Texture，并返回可激活、提交或回滚的替换事务。</summary>
    public TextureReplacementTransaction BeginReplacement(
        IReadOnlyCollection<string> replaceableNames,
        IReadOnlyList<TextureReplacementSource> replacements)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replaceableNames);
        ArgumentNullException.ThrowIfNull(replacements);
        var scope = new HashSet<string>(replaceableNames, StringComparer.Ordinal);
        if (scope.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Replaceable Texture names cannot be empty.", nameof(replaceableNames));

        var staged = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            foreach (TextureReplacementSource replacement in replacements)
            {
                ArgumentNullException.ThrowIfNull(replacement);
                if (string.IsNullOrWhiteSpace(replacement.Name))
                    throw new ArgumentException("Replacement Texture name cannot be empty.", nameof(replacements));
                if (staged.ContainsKey(replacement.Name))
                    throw new ArgumentException(
                        $"Replacement Texture '{replacement.Name}' appears more than once.",
                        nameof(replacements));
                if (_entries.ContainsKey(replacement.Name) && !scope.Contains(replacement.Name))
                    throw new InvalidOperationException(
                        $"Texture '{replacement.Name}' is owned outside the replacement scope.");
                ValidatePixels(
                    replacement.Width,
                    replacement.Height,
                    replacement.RgbaPixels);
                uint handle = _backend.CreateTexture(
                    replacement.Width,
                    replacement.Height,
                    replacement.RgbaPixels,
                    replacement.Sampler);
                if (handle == 0)
                    throw new InvalidOperationException("The texture backend returned an invalid handle.");
                staged.Add(replacement.Name, new Entry(
                    handle,
                    new TextureMetadata(replacement.Width, replacement.Height)));
            }
        }
        catch
        {
            foreach (Entry entry in staged.Values)
                _backend.DeleteTexture(entry.Handle);
            throw;
        }

        var previous = _entries
            .Where(pair => scope.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new TextureReplacementTransaction(this, scope, staged, previous);
    }

    /// <summary>显式捕获 RGBA8 Texture 与 Atlas 页的纯值显存估算。</summary>
    public TextureLibraryDiagnostics CaptureDiagnostics()
    {
        ThrowIfDisposed();
        return new TextureLibraryDiagnostics(_entries
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TextureMemoryDiagnostics(
                pair.Key,
                pair.Value.Metadata.Width,
                pair.Value.Metadata.Height,
                checked((long)pair.Value.Metadata.Width * pair.Value.Metadata.Height * 4L))));
    }

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

    public sealed class TextureReplacementTransaction : IDisposable
    {
        private TextureLibrary? _owner;
        private readonly HashSet<string> _scope;
        private readonly Dictionary<string, Entry> _staged;
        private readonly Dictionary<string, Entry> _previous;
        private bool _active;
        private bool _committed;

        internal TextureReplacementTransaction(
            TextureLibrary owner,
            HashSet<string> scope,
            Dictionary<string, Entry> staged,
            Dictionary<string, Entry> previous)
        {
            _owner = owner;
            _scope = scope;
            _staged = staged;
            _previous = previous;
        }

        public void Activate()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            if (_active) throw new InvalidOperationException("Texture replacement is already active.");
            foreach (string name in _scope) _owner._entries.Remove(name);
            foreach (var pair in _staged) _owner._entries.Add(pair.Key, pair.Value);
            _active = true;
        }

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            if (!_active) throw new InvalidOperationException("Activate the Texture replacement first.");
            foreach (Entry entry in _previous.Values)
                _owner._backend.DeleteTexture(entry.Handle);
            _committed = true;
            _owner = null;
        }

        public void Dispose()
        {
            TextureLibrary? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null || _committed) return;
            if (_active)
            {
                foreach (string name in _staged.Keys) owner._entries.Remove(name);
                foreach (var pair in _previous) owner._entries.Add(pair.Key, pair.Value);
            }
            foreach (Entry entry in _staged.Values)
                owner._backend.DeleteTexture(entry.Handle);
        }
    }
}
