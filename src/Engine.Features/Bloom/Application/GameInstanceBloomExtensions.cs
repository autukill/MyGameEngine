namespace GameEngine.Features.Bloom.Application;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.RenderPipeline.Domain;

public static class GameInstanceBloomExtensions
{
    public static void RequestBloom(
        this GameInstance instance,
        BloomSettings settings,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null,
        RenderSurfaceKey? source = null,
        RenderTargetColorFormat colorFormat = RenderTargetColorFormat.Rgba8,
        RenderSurfaceEncoding encoding = RenderSurfaceEncoding.Display)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        if (!instance.IsActive) return;
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            new BloomEffectDescriptor(
                key ?? BloomEffectDescriptor.DefaultKey,
                settings,
                source,
                colorFormat,
                encoding)));
    }

    public static void ReleaseBloom(
        this GameInstance instance,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        raiseEvent(new RenderEffectReleasedEvent(
            instance.Id,
            key ?? BloomEffectDescriptor.DefaultKey));
    }
}
