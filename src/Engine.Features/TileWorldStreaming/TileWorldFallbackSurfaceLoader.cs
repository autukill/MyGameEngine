namespace GameEngine.Features.TileWorldStreaming;

using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;

/// <summary>
/// Reads and decodes the small, optional full-world fallback set without touching GPU state.
/// </summary>
public sealed class TileWorldFallbackSurfaceLoader
{
    private readonly TileWorldDescriptor _descriptor;
    private readonly IImageDecoder _decoder;
    private readonly string _textureScope;
    private readonly TileWorldChunkLoadMode _loadMode;

    public TileWorldFallbackSurfaceLoader(
        TileWorldDescriptor descriptor,
        string textureScope,
        IImageDecoder? decoder = null,
        TileWorldChunkLoadMode loadMode = TileWorldChunkLoadMode.Background)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        if (string.IsNullOrWhiteSpace(textureScope))
            throw new ArgumentException("Texture scope cannot be empty.", nameof(textureScope));
        if (!Enum.IsDefined(loadMode)) throw new ArgumentOutOfRangeException(nameof(loadMode));
        _decoder = decoder ?? new SkiaImageDecoder();
        _textureScope = textureScope;
        _loadMode = loadMode;
    }

    public ValueTask<TileWorldFallbackSurfaceLease> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loadMode == TileWorldChunkLoadMode.Inline)
            return ValueTask.FromResult(Load(cancellationToken));
        return new ValueTask<TileWorldFallbackSurfaceLease>(Task.Run(
            () => Load(cancellationToken),
            cancellationToken));
    }

    private TileWorldFallbackSurfaceLease Load(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = new TileWorldArchiveReader(File.OpenRead(_descriptor.ArchivePath));
        if (!StringComparer.Ordinal.Equals(_descriptor.Ref.Name, archive.Metadata.Name))
            throw new InvalidDataException("TileWorld descriptor does not match its archive.");
        var prepared = new PreparedFallbackSurface[archive.Metadata.FallbackSurfaces.Count];
        for (int index = 0; index < prepared.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TileWorldFallbackSurfaceMetadata metadata = archive.Metadata.FallbackSurfaces[index];
            TileWorldFallbackSurfaceData surface = archive.ReadFallbackSurface(metadata.LayerIndex);
            using var stream = new MemoryStream(surface.EncodedBytes, writable: false);
            DecodedImage decoded = _decoder.Decode(stream);
            if (decoded.Width != metadata.Width || decoded.Height != metadata.Height ||
                decoded.RgbaPixels.Length != checked(decoded.Width * decoded.Height * 4))
                throw new InvalidDataException(
                    $"TileWorld fallback surface layer {metadata.LayerIndex} decoded to unexpected dimensions.");
            prepared[index] = new PreparedFallbackSurface(
                metadata.LayerIndex,
                metadata.Width,
                metadata.Height,
                metadata.Sampling == TileWorldRasterSampling.PixelArt
                    ? TextureSampler.PixelArt
                    : TextureSampler.Smooth,
                decoded.RgbaPixels);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new TileWorldFallbackSurfaceLease(
            $"{_textureScope}.fallback",
            prepared);
    }
}
