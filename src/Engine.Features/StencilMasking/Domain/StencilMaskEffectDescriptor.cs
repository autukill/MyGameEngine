namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>不携带绘制回调或 GPU 对象的 Stencil Spotlight 描述符。</summary>
public sealed record StencilMaskEffectDescriptor : IRenderEffectDescriptor
{
    private readonly StencilMaskGeometry _firstGeometry;
    private readonly StencilMaskGeometry[]? _geometrySet;

    public const string EffectKind = "stencil-mask";
    public static StencilMaskGroupRef DefaultGroup => StencilMaskGroupRef.Main;
    public static RenderEffectKey DefaultKey => DefaultGroup.Key;
    public static RenderSurfaceKey MaskOutput(RenderEffectKey key) =>
        RenderSurfaceKey.FromEffect(key, "mask");
    public static RenderSurfaceKey MaskOutput(StencilMaskGroupRef group) => group.Output;

    public RenderEffectKey Key { get; }
    public StencilMaskGeometry Geometry => _firstGeometry;
    public int GeometryCount => _geometrySet?.Length ?? 1;
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
        ValidateKey(key);
        if (!geometry.IsValid)
            throw new ArgumentException("Mask geometry must be initialized.", nameof(geometry));
        Key = key;
        _firstGeometry = geometry;
        State = state;
    }

    public StencilMaskEffectDescriptor(
        RenderEffectKey key,
        ReadOnlySpan<StencilMaskGeometry> geometries,
        StencilMaskState state)
    {
        ValidateKey(key);
        if (geometries.IsEmpty)
            throw new ArgumentException("At least one mask geometry is required.", nameof(geometries));
        for (int i = 0; i < geometries.Length; i++)
        {
            if (!geometries[i].IsValid)
                throw new ArgumentException(
                    $"Mask geometry at index {i} must be initialized.", nameof(geometries));
        }

        Key = key;
        _firstGeometry = geometries[0];
        _geometrySet = geometries.Length == 1 ? null : geometries.ToArray();
        State = state;
    }

    public StencilMaskGeometry GetGeometry(int index)
    {
        if ((uint)index >= (uint)GeometryCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _geometrySet is null ? _firstGeometry : _geometrySet[index];
    }

    private static void ValidateKey(RenderEffectKey key)
    {
        if (key.Kind != EffectKind)
            throw new ArgumentException(
                $"Stencil descriptor requires effect kind '{EffectKind}'.", nameof(key));
    }
}
