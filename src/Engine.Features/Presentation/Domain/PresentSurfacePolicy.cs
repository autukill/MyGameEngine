namespace GameEngine.Features.Presentation.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

internal static class PresentSurfacePolicy
{
    public readonly record struct Entry(
        InstanceId FirstOwner,
        RenderSurfaceKey Source,
        ViewportRect Viewport,
        int Layer,
        PresentationBlendMode Blend);

    public static Entry[] ValidateOrderAndDeduplicate(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        if (key != PresentSurfaceDescriptor.DefaultKey)
            throw new ArgumentException("Only the unique present:main terminal is supported.", nameof(key));
        if (owners.Count == 0)
            throw new ArgumentException("Presentation requires at least one owner.", nameof(owners));

        var ordered = new List<Entry>(owners.Count);
        foreach (var (owner, descriptor) in owners)
        {
            if (descriptor is not PresentSurfaceDescriptor present || present.Key != key)
                throw new ArgumentException(
                    "Presentation factory received an incompatible descriptor.", nameof(owners));
            ordered.Add(new Entry(
                owner,
                present.Source,
                present.Viewport,
                present.Layer,
                present.Blend));
        }

        return ordered
            .OrderBy(entry => entry.Layer)
            .ThenBy(entry => entry.FirstOwner)
            .GroupBy(entry => new
            {
                entry.Source,
                entry.Viewport,
                entry.Layer,
                entry.Blend
            })
            .Select(group => group.First())
            .ToArray();
    }
}
