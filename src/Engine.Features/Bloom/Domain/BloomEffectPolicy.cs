namespace GameEngine.Features.Bloom.Domain;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

internal static class BloomEffectPolicy
{
    public readonly record struct Configuration(
        BloomSettings Settings,
        RenderSurfaceKey Source,
        RenderTargetColorFormat ColorFormat,
        RenderSurfaceEncoding Encoding);

    public static Configuration ValidateAndGetConfiguration(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        if (key.Kind != BloomEffectDescriptor.EffectKind)
            throw new ArgumentException("The effect key is not a bloom key.", nameof(key));
        if (owners.Count == 0)
            throw new ArgumentException("A bloom effect requires at least one owner.", nameof(owners));

        Configuration? shared = null;
        foreach (var (_, descriptor) in owners.OrderBy(pair => pair.Key))
        {
            if (descriptor is not BloomEffectDescriptor bloom)
                throw new ArgumentException(
                    "Bloom factory received an incompatible descriptor.", nameof(owners));
            if (bloom.Key != key)
                throw new ArgumentException(
                    "Owner descriptor key does not match the effect key.", nameof(owners));
            var configuration = new Configuration(
                bloom.Settings,
                bloom.Source,
                bloom.ColorFormat,
                bloom.Encoding);
            if (shared is { } existing && existing != configuration)
                throw new InvalidOperationException(
                    $"All owners of shared bloom effect '{key}' must use identical configuration.");
            shared = configuration;
        }
        return shared!.Value;
    }

    public static BloomSettings ValidateAndGetSettings(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
        ValidateAndGetConfiguration(key, owners).Settings;
}
