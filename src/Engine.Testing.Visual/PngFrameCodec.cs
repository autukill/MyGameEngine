namespace GameEngine.Testing.Visual;

using System.Runtime.InteropServices;
using SkiaSharp;

public static class PngFrameCodec
{
    public static void Save(CapturedFrame frame, string path)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);

        var info = new SKImageInfo(
            frame.Width,
            frame.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(frame.RgbaPixels, 0, bitmap.GetPixels(), frame.RgbaPixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        data.SaveTo(output);
    }

    public static CapturedFrame Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var source = File.OpenRead(path);
        using var codec = SKCodec.Create(source) ??
            throw new InvalidDataException($"'{path}' is not a supported image.");
        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        var pixels = new byte[checked(info.Width * info.Height * 4)];
        unsafe
        {
            fixed (byte* pointer = pixels)
            {
                var result = codec.GetPixels(info, (IntPtr)pointer);
                if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
                    throw new InvalidDataException($"Failed to decode '{path}': {result}.");
            }
        }
        return new CapturedFrame(info.Width, info.Height, pixels);
    }
}
