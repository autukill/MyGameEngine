namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

public readonly record struct TileWorldRuntimeFallbackSurface(
    int LayerIndex,
    TextureRef Texture);

internal sealed record PreparedFallbackSurface(
    int LayerIndex,
    int Width,
    int Height,
    TextureSampler Sampler,
    byte[] RgbaPixels);

/// <summary>
/// Owns the optional full-world fallback textures. Decode is CPU-only; CommitTextures is the
/// explicit graphics-thread boundary.
/// </summary>
public sealed class TileWorldFallbackSurfaceLease : IDisposable
{
    private readonly string _textureNamePrefix;
    private PreparedFallbackSurface[]? _prepared;
    private TileWorldRuntimeFallbackSurface[] _surfaces = [];
    private TextureLibrary? _textures;
    private bool _committed;
    private bool _disposed;

    internal TileWorldFallbackSurfaceLease(
        string textureNamePrefix,
        PreparedFallbackSurface[] prepared)
    {
        _textureNamePrefix = textureNamePrefix;
        _prepared = prepared;
    }

    public IReadOnlyList<TileWorldRuntimeFallbackSurface> Surfaces => _surfaces;
    public bool IsCommitted => _committed;
    public bool IsDisposed => _disposed;

    public void CommitTextures(TextureLibrary textures)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(textures);
        if (_committed)
        {
            if (_textures is not null && !ReferenceEquals(_textures, textures))
                throw new InvalidOperationException(
                    "TileWorld fallback surfaces are committed to another TextureLibrary.");
            return;
        }

        PreparedFallbackSurface[] prepared = _prepared ?? [];
        var committed = new TileWorldRuntimeFallbackSurface[prepared.Length];
        int registered = 0;
        try
        {
            for (int index = 0; index < prepared.Length; index++)
            {
                PreparedFallbackSurface surface = prepared[index];
                TextureRef texture = textures.RegisterRgba(
                    $"{_textureNamePrefix}.layer-{surface.LayerIndex}",
                    surface.Width,
                    surface.Height,
                    surface.RgbaPixels,
                    surface.Sampler);
                committed[index] = new TileWorldRuntimeFallbackSurface(
                    surface.LayerIndex,
                    texture);
                registered++;
            }
        }
        catch
        {
            for (int index = 0; index < registered; index++)
                textures.Remove(committed[index].Texture);
            throw;
        }

        _surfaces = committed;
        _prepared = null;
        _textures = textures;
        _committed = true;
    }

    public bool TryGet(int layerIndex, out TileWorldRuntimeFallbackSurface surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < _surfaces.Length; index++)
        {
            if (_surfaces[index].LayerIndex != layerIndex) continue;
            surface = _surfaces[index];
            return true;
        }
        surface = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_textures is not null)
        {
            for (int index = 0; index < _surfaces.Length; index++)
            {
                try { _textures.Remove(_surfaces[index].Texture); }
                catch (ObjectDisposedException) { }
            }
        }
        _surfaces = [];
        _prepared = null;
        _textures = null;
    }
}
