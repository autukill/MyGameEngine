namespace GameEngine.Features.ToneMapping.Application;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Domain;

public static class GameInstanceToneMappingExtensions
{
    public static void RequestToneMapping(
        this GameInstance instance,
        ToneMappingSettings settings,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null,
        RenderSurfaceKey? source = null,
        RenderSurfaceKey? bloomSource = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        if (!instance.IsActive) return;
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            new ToneMappingEffectDescriptor(
                key ?? ToneMappingEffectDescriptor.DefaultKey,
                settings,
                source,
                bloomSource)));
    }

    public static void ReleaseToneMapping(
        this GameInstance instance,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        raiseEvent(new RenderEffectReleasedEvent(
            instance.Id,
            key ?? ToneMappingEffectDescriptor.DefaultKey));
    }
}
