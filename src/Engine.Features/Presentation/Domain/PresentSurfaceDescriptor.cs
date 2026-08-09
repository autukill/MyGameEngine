namespace GameEngine.Features.Presentation.Domain;

using GameEngine.Features.RenderPipeline.Domain;

public enum PresentationBlendMode
{
    Opaque,
    AlphaBlend,
    Additive
}

/// <summary>声明一个 RGBA8/Display Surface 如何进入当前 Scene 的唯一屏幕终端。</summary>
public sealed record PresentSurfaceDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "present";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");

    public RenderEffectKey Key { get; }
    public RenderSurfaceKey Source { get; }
    public ViewportRect Viewport { get; }
    public int Layer { get; }
    public PresentationBlendMode Blend { get; }

    public PresentSurfaceDescriptor(
        RenderEffectKey key,
        RenderSurfaceKey source,
        ViewportRect viewport,
        int layer,
        PresentationBlendMode blend)
    {
        if (key != DefaultKey)
            throw new ArgumentException(
                $"Presentation requires the unique effect key '{DefaultKey}'.", nameof(key));
        if (!source.IsValid)
            throw new ArgumentException("Presentation source must be initialized.", nameof(source));
        ValidateViewport(viewport);
        if (!Enum.IsDefined(blend))
            throw new ArgumentOutOfRangeException(nameof(blend));
        Key = key;
        Source = source;
        Viewport = viewport;
        Layer = layer;
        Blend = blend;
    }

    private static void ValidateViewport(ViewportRect viewport)
    {
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) ||
            !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height) ||
            viewport.X < 0f || viewport.Y < 0f ||
            viewport.Width <= 0f || viewport.Height <= 0f ||
            viewport.X + viewport.Width > 1f || viewport.Y + viewport.Height > 1f)
            throw new ArgumentOutOfRangeException(
                nameof(viewport), "Viewport must be a positive normalized rectangle inside [0,1].");
    }
}
