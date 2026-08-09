namespace GameEngine.Hosting;

using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.ToneMapping.Domain;

/// <summary>
/// Immutable post-processing profile for one Render View. Direct adds no Pass or leased target;
/// Hdr always adds Tone Mapping and optionally adds Bloom.
/// </summary>
public sealed record RenderViewEffects
{
    public static RenderViewEffects Direct { get; } = new(null, null);

    public ToneMappingSettings? ToneMapping { get; }
    public BloomSettings? Bloom { get; }
    public bool IsHdr => ToneMapping.HasValue;
    public int AdditionalPassCount => IsHdr ? 1 + (Bloom.HasValue ? 1 : 0) : 0;
    public int AdditionalRenderTargetCount => IsHdr ? 1 + (Bloom.HasValue ? 3 : 0) : 0;

    private RenderViewEffects(
        ToneMappingSettings? toneMapping,
        BloomSettings? bloom)
    {
        ToneMapping = toneMapping;
        Bloom = bloom;
    }

    public static RenderViewEffects Hdr(
        ToneMappingSettings toneMapping,
        BloomSettings? bloom = null)
    {
        // Record structs can be default-initialized and bypass their public constructors.
        // Reconstruct them here so the declarative boundary remains strict.
        var validatedToneMapping = new ToneMappingSettings(
            toneMapping.Operator,
            toneMapping.Exposure,
            toneMapping.Gamma);
        BloomSettings? validatedBloom = bloom is { } value
            ? new BloomSettings(
                value.Threshold,
                value.Intensity,
                value.BlurRadius,
                value.Iterations,
                value.Resolution)
            : null;
        return new RenderViewEffects(validatedToneMapping, validatedBloom);
    }

    public override string ToString() => IsHdr
        ? Bloom.HasValue ? "HDR + Bloom + Tone Mapping" : "HDR + Tone Mapping"
        : "Direct Display";
}
