namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

internal static class StencilMaskEffectPolicy
{
    public static StencilMaskEffectDescriptor[] ValidateAndOrder(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        if (key.Kind != StencilMaskEffectDescriptor.EffectKind)
            throw new ArgumentException("The effect key is not a stencil-mask key.", nameof(key));
        if (owners.Count == 0)
            throw new ArgumentException("A stencil effect requires at least one owner.", nameof(owners));

        var descriptors = new List<(InstanceId Owner, StencilMaskEffectDescriptor Descriptor)>(owners.Count);
        foreach (var (owner, descriptor) in owners)
        {
            if (descriptor is not StencilMaskEffectDescriptor stencil)
                throw new ArgumentException("Stencil factory received an incompatible descriptor.", nameof(owners));
            if (stencil.Key != key)
                throw new ArgumentException("Owner descriptor key does not match the effect key.", nameof(owners));
            descriptors.Add((owner, stencil));
        }

        var sharedState = descriptors[0].Descriptor.State;
        if (descriptors.Any(item => item.Descriptor.State != sharedState))
            throw new InvalidOperationException(
                $"All owners of shared stencil effect '{key}' must use the same stencil state.");

        return descriptors
            .OrderBy(item => item.Owner)
            .Select(item => item.Descriptor)
            .ToArray();
    }
}
