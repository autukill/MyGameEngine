namespace GameEngine.Features.TextureAssets.Infrastructure;

using GameEngine.Features.TextureAssets.Domain;
using SkiaSharp;

/// <summary>Decodes PNG, static WebP, and the other raster formats supported by Skia.</summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
    public const int DefaultMaxDimension = 16_384;
    public const long DefaultMaxPixelCount = 268_435_456;

    private readonly int _maxDimension;
    private readonly long _maxPixelCount;

    public SkiaImageDecoder(
        int maxDimension = DefaultMaxDimension,
        long maxPixelCount = DefaultMaxPixelCount)
    {
        if (maxDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        if (maxPixelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPixelCount));

        _maxDimension = maxDimension;
        _maxPixelCount = maxPixelCount;
    }

    public unsafe DecodedImage Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The image stream must be readable.", nameof(stream));

        using var data = SKData.Create(stream);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The stream is not a supported image.");

        int width = codec.Info.Width;
        int height = codec.Info.Height;
        ValidateDimensions(width, height);
        if (codec.FrameCount > 1)
            throw new NotSupportedException("Animated image assets are not supported; use Sprite frames instead.");

        var imageInfo = new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));

        fixed (byte* pixelPointer = pixels)
        {
            var result = codec.GetPixels(imageInfo, (IntPtr)pixelPointer);
            if (result != SKCodecResult.Success)
                throw new InvalidDataException($"Image decoding failed with result '{result}'.");
        }

        return new DecodedImage(width, height, pixels);
    }

    private void ValidateDimensions(int width, int height)
    {
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 ||
            width > _maxDimension || height > _maxDimension || pixels > _maxPixelCount)
        {
            throw new InvalidDataException(
                $"Image dimensions {width}x{height} exceed the configured decode limits.");
        }
    }
}
