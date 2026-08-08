namespace GameEngine.Testing.Visual;

public sealed record CapturedFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] RgbaPixels { get; }

    public CapturedFrame(int width, int height, byte[] rgbaPixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        int expected = checked(width * height * 4);
        if (rgbaPixels.Length != expected)
            throw new ArgumentException($"RGBA data must contain exactly {expected} bytes.", nameof(rgbaPixels));
        Width = width;
        Height = height;
        RgbaPixels = rgbaPixels;
    }
}
