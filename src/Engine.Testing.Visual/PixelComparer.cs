namespace GameEngine.Testing.Visual;

public readonly record struct PixelComparisonOptions(
    byte SoftChannelDelta = 2,
    byte HardChannelDelta = 8,
    double MaximumDifferentPixelRatio = 0.0025)
{
    public static PixelComparisonOptions Default => new(
        SoftChannelDelta: 2,
        HardChannelDelta: 8,
        MaximumDifferentPixelRatio: 0.0025);
}

public sealed record PixelComparisonResult(
    bool IsMatch,
    int TotalPixels,
    int DifferentPixels,
    double DifferentPixelRatio,
    byte MaximumChannelDelta,
    string? FailureReason,
    CapturedFrame? DifferenceFrame);

public static class PixelComparer
{
    public static PixelComparisonResult Compare(
        CapturedFrame expected,
        CapturedFrame actual,
        PixelComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var tolerance = options ?? PixelComparisonOptions.Default;
        if (tolerance.MaximumDifferentPixelRatio is < 0 or > 1 ||
            !double.IsFinite(tolerance.MaximumDifferentPixelRatio))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (tolerance.HardChannelDelta < tolerance.SoftChannelDelta)
            throw new ArgumentException("Hard delta cannot be smaller than soft delta.", nameof(options));

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new PixelComparisonResult(
                false,
                0,
                0,
                1,
                byte.MaxValue,
                $"Image size differs: expected {expected.Width}x{expected.Height}, " +
                $"actual {actual.Width}x{actual.Height}.",
                null);
        }

        int pixels = expected.Width * expected.Height;
        int different = 0;
        byte maximum = 0;
        var diff = new byte[expected.RgbaPixels.Length];
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            int offset = pixel * 4;
            bool bothTransparent = expected.RgbaPixels[offset + 3] == 0 &&
                                   actual.RgbaPixels[offset + 3] == 0;
            byte pixelMaximum = 0;
            for (int channel = 0; channel < 4; channel++)
            {
                if (bothTransparent && channel < 3) continue;
                int delta = Math.Abs(
                    expected.RgbaPixels[offset + channel] -
                    actual.RgbaPixels[offset + channel]);
                pixelMaximum = Math.Max(pixelMaximum, (byte)delta);
            }

            maximum = Math.Max(maximum, pixelMaximum);
            if (pixelMaximum > tolerance.SoftChannelDelta) different++;
            byte heat = (byte)Math.Min(255, pixelMaximum * 32);
            diff[offset] = heat;
            diff[offset + 1] = 0;
            diff[offset + 2] = 0;
            diff[offset + 3] = 255;
        }

        double ratio = pixels == 0 ? 0 : (double)different / pixels;
        bool match = maximum <= tolerance.HardChannelDelta &&
                     ratio <= tolerance.MaximumDifferentPixelRatio;
        string? reason = match
            ? null
            : $"Pixel difference exceeded tolerance: maximum channel delta={maximum}, " +
              $"different pixels={different}/{pixels} ({ratio:P4}).";
        return new PixelComparisonResult(
            match,
            pixels,
            different,
            ratio,
            maximum,
            reason,
            new CapturedFrame(expected.Width, expected.Height, diff));
    }
}
