namespace GameEngine.Features.Bloom.Domain;

using GameEngine.Features.RenderPipeline.Domain;

public enum BloomPresentation
{
    Additive,
    SurfaceOnly
}

public sealed record BloomEffectDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "bloom";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");
    public static RenderSurfaceKey GlowOutput(RenderEffectKey key) =>
        RenderSurfaceKey.FromEffect(key, "glow");

    public RenderEffectKey Key { get; }
    public BloomSettings Settings { get; }
    public RenderSurfaceKey Source { get; }
    public RenderTargetColorFormat ColorFormat { get; }
    public RenderSurfaceEncoding Encoding { get; }
    public BloomPresentation Presentation { get; }

    public BloomEffectDescriptor(
        RenderEffectKey key,
        BloomSettings settings,
        RenderSurfaceKey? source = null,
        RenderTargetColorFormat colorFormat = RenderTargetColorFormat.Rgba8,
        RenderSurfaceEncoding encoding = RenderSurfaceEncoding.Display,
        BloomPresentation presentation = BloomPresentation.Additive)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException(
                $"Bloom descriptor requires effect kind '{EffectKind}'.", nameof(key));
        Key = key;
        Settings = settings;
        Source = source ?? RenderSurfaceKey.SceneColor;
        if (!Enum.IsDefined(colorFormat))
            throw new ArgumentOutOfRangeException(nameof(colorFormat));
        if (!Enum.IsDefined(encoding))
            throw new ArgumentOutOfRangeException(nameof(encoding));
        if (!Enum.IsDefined(presentation))
            throw new ArgumentOutOfRangeException(nameof(presentation));
        if ((colorFormat == RenderTargetColorFormat.Rgba16Float) !=
            (encoding == RenderSurfaceEncoding.Linear))
            throw new ArgumentException(
                "HDR Bloom must use Linear encoding; LDR Bloom must use Display encoding.");
        ColorFormat = colorFormat;
        Encoding = encoding;
        Presentation = presentation;
    }
}
