namespace GameEngine.Features.Bloom.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

internal static class BloomEffectPolicy
{
    public static BloomSettings ValidateAndGetSettings(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        if (key.Kind != BloomEffectDescriptor.EffectKind)
            throw new ArgumentException("The effect key is not a bloom key.", nameof(key));
        if (owners.Count == 0)
            throw new ArgumentException("A bloom effect requires at least one owner.", nameof(owners));

        BloomSettings? shared = null;
        foreach (var (_, descriptor) in owners.OrderBy(pair => pair.Key))
        {
            if (descriptor is not BloomEffectDescriptor bloom)
                throw new ArgumentException(
                    "Bloom factory received an incompatible descriptor.", nameof(owners));
            if (bloom.Key != key)
                throw new ArgumentException(
                    "Owner descriptor key does not match the effect key.", nameof(owners));
            if (shared is { } settings && settings != bloom.Settings)
                throw new InvalidOperationException(
                    $"All owners of shared bloom effect '{key}' must use identical settings.");
            shared = bloom.Settings;
        }
        return shared!.Value;
    }
}
