namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// A logical stencil-mask group. All owners in one group share a Pass, RenderTarget, state,
/// and output surface.
/// </summary>
public readonly record struct StencilMaskGroupRef
{
    public static StencilMaskGroupRef Main => new("main");

    public RenderEffectKey Key { get; }
    public string Slot => Key.Slot;
    public RenderSurfaceKey Output => StencilMaskEffectDescriptor.MaskOutput(Key);

    public StencilMaskGroupRef(string slot) =>
        Key = new RenderEffectKey(StencilMaskEffectDescriptor.EffectKind, slot);

    public StencilMaskGroupRef(RenderEffectKey key)
    {
        if (key.Kind != StencilMaskEffectDescriptor.EffectKind)
            throw new ArgumentException(
                $"Stencil mask groups require effect kind '{StencilMaskEffectDescriptor.EffectKind}'.",
                nameof(key));
        Key = key;
    }

    public override string ToString() => Key.ToString();
}
