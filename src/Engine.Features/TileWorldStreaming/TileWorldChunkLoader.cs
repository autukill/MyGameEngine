namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.WorldStreaming;

/// <summary>
/// Random-reads one fixed TileWorld level. Archive IO and WebP decode may run in the background;
/// GPU upload remains a separate TileWorldChunkLease.CommitTextures call.
/// </summary>
public sealed class TileWorldChunkLoader :
    IWorldChunkLoader<TileWorldChunkLease>, IDisposable
{
    private readonly TileWorldArchiveReader _archive;
    private readonly IImageDecoder _decoder;
    private readonly string _textureScope;
    private readonly TileWorldChunkLoadMode _loadMode;
    private readonly TileWorldBackgroundScheduler? _backgroundScheduler;
    private bool _disposed;

    public TileWorldChunkLoader(
        TileWorldDescriptor descriptor,
        int level,
        string textureScope,
        IImageDecoder? decoder = null,
        TileWorldChunkLoadMode loadMode = TileWorldChunkLoadMode.Background)
        : this(descriptor, level, textureScope, decoder, loadMode, null)
    {
    }

    internal TileWorldChunkLoader(
        TileWorldDescriptor descriptor,
        int level,
        string textureScope,
        IImageDecoder? decoder,
        TileWorldChunkLoadMode loadMode,
        TileWorldBackgroundScheduler? backgroundScheduler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if ((uint)level >= (uint)descriptor.Metadata.DeclaredLodCount)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (string.IsNullOrWhiteSpace(textureScope))
            throw new ArgumentException("Texture scope cannot be empty.", nameof(textureScope));
        if (!Enum.IsDefined(loadMode)) throw new ArgumentOutOfRangeException(nameof(loadMode));
        _archive = new TileWorldArchiveReader(File.OpenRead(descriptor.ArchivePath));
        _decoder = decoder ?? new SkiaImageDecoder();
        _textureScope = textureScope;
        _loadMode = loadMode;
        _backgroundScheduler = backgroundScheduler;
        Level = level;
        Metadata = _archive.Metadata;
        if (!StringComparer.Ordinal.Equals(descriptor.Ref.Name, Metadata.Name))
        {
            _archive.Dispose();
            throw new InvalidDataException("TileWorld descriptor does not match its archive.");
        }
    }

    public int Level { get; }
    public TileWorldMetadata Metadata { get; }

    public ValueTask<TileWorldChunkLease> LoadAsync(
        WorldChunkCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loadMode == TileWorldChunkLoadMode.Inline)
            return ValueTask.FromResult(Load(coordinate, cancellationToken));
        if (_backgroundScheduler is not null)
            return new ValueTask<TileWorldChunkLease>(_backgroundScheduler.RunAsync(
                () => Load(coordinate, cancellationToken),
                cancellationToken));
        return new ValueTask<TileWorldChunkLease>(Task.Run(
            () => Load(coordinate, cancellationToken),
            cancellationToken));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _archive.Dispose();
    }

    private TileWorldChunkLease Load(
        WorldChunkCoordinate coordinate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new TileWorldChunkKey(Level, coordinate.X, coordinate.Y);
        TileWorldChunkBounds bounds = Metadata.GetChunkBounds(Level);
        if (!bounds.Contains(coordinate.X, coordinate.Y))
            throw new ArgumentOutOfRangeException(
                nameof(coordinate),
                $"Chunk '{key}' is outside TileWorld bounds.");
        string prefix = $"{_textureScope}.L{Level}.x{coordinate.X}.y{coordinate.Y}";
        TextureSampler sampler = Metadata.RasterSettings.Sampling == TileWorldRasterSampling.PixelArt
            ? TextureSampler.PixelArt
            : TextureSampler.Smooth;
        if (!_archive.Contains(key))
            return new TileWorldChunkLease(key, false, null, [], prefix, sampler);

        if (Level == 0)
        {
            TileWorldChunkData data = _archive.ReadChunk(key);
            cancellationToken.ThrowIfCancellationRequested();
            return new TileWorldChunkLease(key, true, data, [], prefix, sampler);
        }

        TileWorldRasterChunkData raster = _archive.ReadRasterChunk(key);
        var prepared = new PreparedRasterLayer[raster.Layers.Count];
        for (int index = 0; index < prepared.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TileWorldRasterLayerData layer = raster.Layers[index];
            using var stream = new MemoryStream(layer.EncodedBytes, writable: false);
            DecodedImage decoded = _decoder.Decode(stream);
            if (decoded.Width != layer.EncodedWidth || decoded.Height != layer.EncodedHeight ||
                decoded.RgbaPixels.Length != checked(decoded.Width * decoded.Height * 4))
                throw new InvalidDataException(
                    $"TileWorld Raster layer {layer.LayerIndex} decoded to unexpected dimensions.");
            prepared[index] = new PreparedRasterLayer(
                layer.LayerIndex,
                layer.Width,
                layer.Height,
                layer.Gutter,
                decoded.RgbaPixels);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new TileWorldChunkLease(key, true, null, prepared, prefix, sampler);
    }
}
