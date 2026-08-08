namespace GameEngine.Features.Bloom.Domain;

public enum BloomResolution
{
    Full = 1,
    Half = 2,
    Quarter = 4
}

public readonly record struct BloomSettings
{
    public static BloomSettings Default => new(
        threshold: 0.35f,
        intensity: 1.25f,
        blurRadius: 1f,
        iterations: 2,
        resolution: BloomResolution.Half);

    public float Threshold { get; }
    public float Intensity { get; }
    public float BlurRadius { get; }
    public int Iterations { get; }
    public BloomResolution Resolution { get; }

    public BloomSettings(
        float threshold,
        float intensity,
        float blurRadius,
        int iterations,
        BloomResolution resolution)
    {
        if (!float.IsFinite(threshold) || threshold is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        if (!float.IsFinite(intensity) || intensity <= 0f || intensity > 8f)
            throw new ArgumentOutOfRangeException(nameof(intensity));
        if (!float.IsFinite(blurRadius) || blurRadius <= 0f || blurRadius > 4f)
            throw new ArgumentOutOfRangeException(nameof(blurRadius));
        if (iterations is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(iterations));
        if (!Enum.IsDefined(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution));

        Threshold = threshold;
        Intensity = intensity;
        BlurRadius = blurRadius;
        Iterations = iterations;
        Resolution = resolution;
    }
}
