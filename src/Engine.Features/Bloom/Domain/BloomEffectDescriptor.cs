namespace GameEngine.Features.Bloom.Domain;

using GameEngine.Features.RenderPipeline.Domain;

public sealed record BloomEffectDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "bloom";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");
    public static RenderSurfaceKey GlowOutput(RenderEffectKey key) =>
        RenderSurfaceKey.FromEffect(key, "glow");

    public RenderEffectKey Key { get; }
    public BloomSettings Settings { get; }
    public RenderSurfaceKey Source { get; }

    public BloomEffectDescriptor(
        RenderEffectKey key,
        BloomSettings settings,
        RenderSurfaceKey? source = null)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException(
                $"Bloom descriptor requires effect kind '{EffectKind}'.", nameof(key));
        Key = key;
        Settings = settings;
        Source = source ?? RenderSurfaceKey.SceneColor;
    }
}
