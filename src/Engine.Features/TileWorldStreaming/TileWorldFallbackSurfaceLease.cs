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
/// Owns the optional full-world fallback textures. Decode is CPU-only; eager and incremental
/// commits share the same explicit graphics-thread boundary.
/// </summary>
public sealed class TileWorldFallbackSurfaceLease : IDisposable
{
    private readonly string _textureNamePrefix;
    private PreparedFallbackSurface[]? _prepared;
    private TileWorldRuntimeFallbackSurface[] _surfaces = [];
    private TileWorldRuntimeFallbackSurface[]? _stagedSurfaces;
    private TextureLibrary? _textures;
    private int _nextPreparedSurface;
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
        var budget = TileWorldTextureUploadBudgetState.Unlimited;
        while (!_committed) TryCommitNextTexture(textures, ref budget);
    }

    internal bool TryCommitNextTexture(
        TextureLibrary textures,
        ref TileWorldTextureUploadBudgetState budget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(textures);
        if (_committed)
        {
            ValidateTextureLibrary(textures);
            return false;
        }
        ValidateTextureLibrary(textures);

        PreparedFallbackSurface[] prepared = _prepared ?? [];
        if (prepared.Length == 0)
        {
            _prepared = null;
            _textures = textures;
            _committed = true;
            return false;
        }

        PreparedFallbackSurface surface = prepared[_nextPreparedSurface];
        long bytes = checked((long)surface.Width * surface.Height * 4L);
        if (!budget.TryReserve(bytes)) return false;

        _stagedSurfaces ??= new TileWorldRuntimeFallbackSurface[prepared.Length];
        _textures = textures;
        try
        {
            TextureRef texture = textures.RegisterRgba(
                $"{_textureNamePrefix}.layer-{surface.LayerIndex}",
                surface.Width,
                surface.Height,
                surface.RgbaPixels,
                surface.Sampler);
            _stagedSurfaces[_nextPreparedSurface] = new TileWorldRuntimeFallbackSurface(
                surface.LayerIndex,
                texture);
            _nextPreparedSurface++;
        }
        catch
        {
            RollBackStagedTextures();
            throw;
        }

        if (_nextPreparedSurface == prepared.Length)
        {
            _surfaces = _stagedSurfaces;
            _stagedSurfaces = null;
            _prepared = null;
            _committed = true;
        }
        return true;
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
            if (_stagedSurfaces is not null)
            {
                for (int index = 0; index < _nextPreparedSurface; index++)
                {
                    try { _textures.Remove(_stagedSurfaces[index].Texture); }
                    catch (ObjectDisposedException) { }
                }
            }
        }
        _surfaces = [];
        _stagedSurfaces = null;
        _prepared = null;
        _textures = null;
        _nextPreparedSurface = 0;
    }

    private void ValidateTextureLibrary(TextureLibrary textures)
    {
        if (_textures is not null && !ReferenceEquals(_textures, textures))
            throw new InvalidOperationException(
                "TileWorld fallback surfaces are committed to another TextureLibrary.");
    }

    private void RollBackStagedTextures()
    {
        if (_textures is not null && _stagedSurfaces is not null)
        {
            for (int index = 0; index < _nextPreparedSurface; index++)
                _textures.Remove(_stagedSurfaces[index].Texture);
        }
        _stagedSurfaces = null;
        _textures = null;
        _nextPreparedSurface = 0;
    }
}
