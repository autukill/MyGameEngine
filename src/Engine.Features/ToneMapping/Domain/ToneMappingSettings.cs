namespace GameEngine.Features.ToneMapping.Domain;

public enum ToneMappingOperator
{
    Aces,
    Reinhard
}

public readonly record struct ToneMappingSettings
{
    public static ToneMappingSettings Default => new(
        ToneMappingOperator.Aces,
        exposure: 0f,
        gamma: 2.2f);

    public ToneMappingOperator Operator { get; }
    public float Exposure { get; }
    public float Gamma { get; }

    public ToneMappingSettings(
        ToneMappingOperator @operator,
        float exposure,
        float gamma)
    {
        if (!Enum.IsDefined(@operator))
            throw new ArgumentOutOfRangeException(nameof(@operator));
        if (!float.IsFinite(exposure) || exposure is < -10f or > 10f)
            throw new ArgumentOutOfRangeException(nameof(exposure));
        if (!float.IsFinite(gamma) || gamma <= 0f || gamma > 4f)
            throw new ArgumentOutOfRangeException(nameof(gamma));
        Operator = @operator;
        Exposure = exposure;
        Gamma = gamma;
    }
}
