namespace GameEngine.Features.TileWorldStreaming;

using System.Numerics;
using System.Runtime.CompilerServices;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.Tilemaps.Domain;
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
/// Owns one decoded TileWorld Chunk. Background loading only prepares CPU data. The eager public
/// CommitTextures path and the Session's incremental budgeted path both run on the graphics thread.
/// </summary>
public sealed class TileWorldChunkLease : IDisposable
{
    private readonly string _textureNamePrefix;
    private readonly TextureSampler _sampler;
    private PreparedRasterLayer[]? _preparedLayers;
    private TileWorldRuntimeRasterLayer[] _rasterLayers = [];
    private TileWorldRuntimeRasterLayer[]? _stagedRasterLayers;
    private TextureLibrary? _textureLibrary;
    private TileWorldChunkData? _authoritativeData;
    private long _preparedDecodedBytes;
    private long _estimatedGpuTextureBytes;
    private int _nextPreparedLayer;
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
        _authoritativeData = authoritativeData;
        _preparedLayers = preparedLayers;
        for (int index = 0; index < preparedLayers.Length; index++)
            _preparedDecodedBytes = checked(
                _preparedDecodedBytes + preparedLayers[index].RgbaPixels.LongLength);
        _textureNamePrefix = textureNamePrefix;
        _sampler = sampler;
    }

    public TileWorldChunkKey Key { get; }
    public bool HasPayload { get; }
    public TileWorldChunkData? AuthoritativeData => _authoritativeData;
    public IReadOnlyList<TileWorldRuntimeRasterLayer> RasterLayers => _rasterLayers;
    public bool IsCommitted => _committed;
    public bool IsDisposed => _disposed;
    public long PreparedDecodedBytes => _preparedDecodedBytes;
    public long EstimatedGpuTextureBytes => _estimatedGpuTextureBytes;
    public long EstimatedAuthoritativePayloadBytes =>
        EstimateAuthoritativePayloadBytes(_authoritativeData);

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

        PreparedRasterLayer[] prepared = _preparedLayers ?? [];
        if (prepared.Length == 0)
        {
            _preparedLayers = null;
            _textureLibrary = textures;
            _committed = true;
            return false;
        }

        PreparedRasterLayer layer = prepared[_nextPreparedLayer];
        int encodedWidth = checked(layer.Width + layer.Gutter * 2);
        int encodedHeight = checked(layer.Height + layer.Gutter * 2);
        long bytes = checked((long)encodedWidth * encodedHeight * 4L);
        if (!budget.TryReserve(bytes)) return false;

        _stagedRasterLayers ??= new TileWorldRuntimeRasterLayer[prepared.Length];
        _textureLibrary = textures;
        try
        {
            string name = $"{_textureNamePrefix}.layer-{layer.LayerIndex}";
            TextureRef texture = textures.RegisterRgba(
                name,
                encodedWidth,
                encodedHeight,
                layer.RgbaPixels,
                _sampler);
            float left = (float)layer.Gutter / encodedWidth;
            float top = (float)layer.Gutter / encodedHeight;
            _stagedRasterLayers[_nextPreparedLayer] = new TileWorldRuntimeRasterLayer(
                layer.LayerIndex,
                texture,
                new Vector4(
                    left,
                    top,
                    (float)(layer.Gutter + layer.Width) / encodedWidth,
                    (float)(layer.Gutter + layer.Height) / encodedHeight));
            _nextPreparedLayer++;
            _estimatedGpuTextureBytes = checked(_estimatedGpuTextureBytes + bytes);
        }
        catch
        {
            RollBackStagedTextures();
            throw;
        }

        if (_nextPreparedLayer == prepared.Length)
        {
            _rasterLayers = _stagedRasterLayers;
            _stagedRasterLayers = null;
            _preparedLayers = null;
            _preparedDecodedBytes = 0;
            _committed = true;
        }
        return true;
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
            if (_stagedRasterLayers is not null)
            {
                for (int index = 0; index < _nextPreparedLayer; index++)
                {
                    try { textures.Remove(_stagedRasterLayers[index].Texture); }
                    catch (ObjectDisposedException) { }
                }
            }
        }
        _rasterLayers = [];
        _stagedRasterLayers = null;
        _preparedLayers = null;
        _authoritativeData = null;
        _textureLibrary = null;
        _nextPreparedLayer = 0;
        _preparedDecodedBytes = 0;
        _estimatedGpuTextureBytes = 0;
    }

    private void ValidateTextureLibrary(TextureLibrary textures)
    {
        if (_textureLibrary is not null && !ReferenceEquals(_textureLibrary, textures))
            throw new InvalidOperationException("TileWorld Chunk is committed to another TextureLibrary.");
    }

    private void RollBackStagedTextures()
    {
        if (_textureLibrary is not null && _stagedRasterLayers is not null)
        {
            for (int index = 0; index < _nextPreparedLayer; index++)
                _textureLibrary.Remove(_stagedRasterLayers[index].Texture);
        }
        _stagedRasterLayers = null;
        _textureLibrary = null;
        _nextPreparedLayer = 0;
        _estimatedGpuTextureBytes = 0;
    }

    private static long EstimateAuthoritativePayloadBytes(TileWorldChunkData? data)
    {
        if (data is null) return 0;
        long total = 0;
        for (int index = 0; index < data.Layers.Count; index++)
        {
            TileWorldChunkLayerData layer = data.Layers[index];
            total = checked(total +
                layer.Cells.LongLength * Unsafe.SizeOf<TileCell>() +
                layer.CollisionRects.LongLength * Unsafe.SizeOf<TileWorldCollisionRect>());
        }
        return total;
    }
}
