namespace GameEngine.Features.ToneMapping.Domain;

using GameEngine.Features.RenderPipeline.Domain;

public sealed record ToneMappingEffectDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "toneMapping";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");
    public static RenderSurfaceKey ColorOutput(RenderEffectKey key) =>
        RenderSurfaceKey.FromEffect(key, "color");

    public RenderEffectKey Key { get; }
    public ToneMappingSettings Settings { get; }
    public RenderSurfaceKey Source { get; }
    public RenderSurfaceKey? BloomSource { get; }

    public ToneMappingEffectDescriptor(
        RenderEffectKey key,
        ToneMappingSettings settings,
        RenderSurfaceKey? source = null,
        RenderSurfaceKey? bloomSource = null)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException(
                $"Tone Mapping descriptor requires effect kind '{EffectKind}'.", nameof(key));
        if (bloomSource is { } bloom && !bloom.IsValid)
            throw new ArgumentException("Bloom source must be initialized.", nameof(bloomSource));
        Key = key;
        Settings = settings;
        Source = source ?? RenderSurfaceKey.SceneColor;
        BloomSource = bloomSource;
    }
}
