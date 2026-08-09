namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>不携带绘制回调或 GPU 对象的 Stencil Spotlight 描述符。</summary>
public sealed record StencilMaskEffectDescriptor : IRenderEffectDescriptor
{
    public const string EffectKind = "stencil-mask";
    public static RenderEffectKey DefaultKey => new(EffectKind, "main");
    public static RenderSurfaceKey MaskOutput(RenderEffectKey key) =>
        RenderSurfaceKey.FromEffect(key, "mask");

    public RenderEffectKey Key { get; }
    public StencilMaskGeometry Geometry { get; }
    public Vector2D Center => Geometry.Center;
    public float Radius => Geometry.Radius;
    public StencilMaskState State { get; }

    public StencilMaskEffectDescriptor(
        RenderEffectKey key,
        Vector2D center,
        float radius,
        StencilMaskState state)
        : this(key, StencilMaskGeometry.Circle(center, radius), state)
    {
    }

    public StencilMaskEffectDescriptor(
        RenderEffectKey key,
        StencilMaskGeometry geometry,
        StencilMaskState state)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException($"Stencil descriptor requires effect kind '{EffectKind}'.", nameof(key));
        if (!geometry.IsValid)
            throw new ArgumentException("Mask geometry must be initialized.", nameof(geometry));
        Key = key;
        Geometry = geometry;
        State = state;
    }
}
