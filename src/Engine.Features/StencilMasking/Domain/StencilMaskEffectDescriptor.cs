namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>不携带绘制回调或 GPU 对象的 Stencil Spotlight 描述符。</summary>
public sealed record StencilMaskEffectDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "stencil-mask";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");

    public RenderEffectKey Key { get; }
    public Vector2D Center { get; }
    public float Radius { get; }
    public StencilMaskState State { get; }

    public StencilMaskEffectDescriptor(
        RenderEffectKey key,
        Vector2D center,
        float radius,
        StencilMaskState state)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException($"Stencil descriptor requires effect kind '{EffectKind}'.", nameof(key));
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
            throw new ArgumentException("Mask center must be finite.", nameof(center));
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        Key = key;
        Center = center;
        Radius = radius;
        State = state;
    }
}
