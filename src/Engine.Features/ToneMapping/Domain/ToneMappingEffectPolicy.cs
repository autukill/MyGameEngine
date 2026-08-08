namespace GameEngine.Features.ToneMapping.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

internal static class ToneMappingEffectPolicy
{
    public readonly record struct Configuration(
        ToneMappingSettings Settings,
        RenderSurfaceKey Source,
        RenderSurfaceKey? BloomSource);

    public static Configuration ValidateAndGetConfiguration(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        if (key.Kind != ToneMappingEffectDescriptor.EffectKind)
            throw new ArgumentException("The effect key is not a Tone Mapping key.", nameof(key));
        if (owners.Count == 0)
            throw new ArgumentException("Tone Mapping requires at least one owner.", nameof(owners));

        Configuration? shared = null;
        foreach (var (_, descriptor) in owners.OrderBy(pair => pair.Key))
        {
            if (descriptor is not ToneMappingEffectDescriptor toneMapping || toneMapping.Key != key)
                throw new ArgumentException(
                    "Tone Mapping factory received an incompatible descriptor.", nameof(owners));
            var configuration = new Configuration(
                toneMapping.Settings,
                toneMapping.Source,
                toneMapping.BloomSource);
            if (shared is { } existing && existing != configuration)
                throw new InvalidOperationException(
                    $"All owners of shared Tone Mapping effect '{key}' must use identical configuration.");
            shared = configuration;
        }
        return shared!.Value;
    }
}
