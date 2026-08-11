namespace GameEngine.Features.TileWorldStreaming;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;

public readonly record struct TileWorldRuntimeRasterLayer(
    int LayerIndex,
    TextureRef Texture,
    Vector4 InnerUvBounds);

internal sealed record PreparedRasterLayer(
    int LayerIndex,
    int Width,
    int Height,
    int Gutter,
    byte[] RgbaPixels);

/// <summary>
/// Owns one decoded TileWorld Chunk. Background loading only prepares CPU data; CommitTextures is
/// intentionally explicit and must be called on the graphics-context thread.
/// </summary>
public sealed class TileWorldChunkLease : IDisposable
{
    private readonly string _textureNamePrefix;
    private readonly TextureSampler _sampler;
    private PreparedRasterLayer[]? _preparedLayers;
    private TileWorldRuntimeRasterLayer[] _rasterLayers = [];
    private TextureLibrary? _textureLibrary;
    private bool _committed;
    private bool _disposed;

    internal TileWorldChunkLease(
        TileWorldChunkKey key,
        bool hasPayload,
        TileWorldChunkData? authoritativeData,
        PreparedRasterLayer[] preparedLayers,
        string textureNamePrefix,
        TextureSampler sampler)
    {
        Key = key;
        HasPayload = hasPayload;
        AuthoritativeData = authoritativeData;
        _preparedLayers = preparedLayers;
        _textureNamePrefix = textureNamePrefix;
        _sampler = sampler;
    }

    public TileWorldChunkKey Key { get; }
    public bool HasPayload { get; }
    public TileWorldChunkData? AuthoritativeData { get; }
    public IReadOnlyList<TileWorldRuntimeRasterLayer> RasterLayers => _rasterLayers;
    public bool IsCommitted => _committed;
    public bool IsDisposed => _disposed;

    public void CommitTextures(TextureLibrary textures)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(textures);
        if (_committed)
        {
            if (_textureLibrary is not null && !ReferenceEquals(_textureLibrary, textures))
                throw new InvalidOperationException("TileWorld Chunk is committed to another TextureLibrary.");
            return;
        }

        PreparedRasterLayer[] prepared = _preparedLayers ?? [];
        if (prepared.Length == 0)
        {
            _preparedLayers = null;
            _committed = true;
            return;
        }

        var committed = new TileWorldRuntimeRasterLayer[prepared.Length];
        int registered = 0;
        try
        {
            for (int index = 0; index < prepared.Length; index++)
            {
                PreparedRasterLayer layer = prepared[index];
                int encodedWidth = checked(layer.Width + layer.Gutter * 2);
                int encodedHeight = checked(layer.Height + layer.Gutter * 2);
                string name = $"{_textureNamePrefix}.layer-{layer.LayerIndex}";
                TextureRef texture = textures.RegisterRgba(
                    name,
                    encodedWidth,
                    encodedHeight,
                    layer.RgbaPixels,
                    _sampler);
                float left = (float)layer.Gutter / encodedWidth;
                float top = (float)layer.Gutter / encodedHeight;
                committed[index] = new TileWorldRuntimeRasterLayer(
                    layer.LayerIndex,
                    texture,
                    new Vector4(
                        left,
                        top,
                        (float)(layer.Gutter + layer.Width) / encodedWidth,
                        (float)(layer.Gutter + layer.Height) / encodedHeight));
                registered++;
            }
        }
        catch
        {
            for (int index = 0; index < registered; index++)
                textures.Remove(committed[index].Texture);
            throw;
        }

        _rasterLayers = committed;
        _preparedLayers = null;
        _textureLibrary = textures;
        _committed = true;
    }

    public bool TryGetRasterLayer(int layerIndex, out TileWorldRuntimeRasterLayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int index = 0; index < _rasterLayers.Length; index++)
        {
            if (_rasterLayers[index].LayerIndex != layerIndex) continue;
            layer = _rasterLayers[index];
            return true;
        }
        layer = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TextureLibrary? textures = _textureLibrary;
        if (textures is not null)
        {
            for (int index = 0; index < _rasterLayers.Length; index++)
            {
                try { textures.Remove(_rasterLayers[index].Texture); }
                catch (ObjectDisposedException) { }
            }
        }
        _rasterLayers = [];
        _preparedLayers = null;
        _textureLibrary = null;
    }
}
