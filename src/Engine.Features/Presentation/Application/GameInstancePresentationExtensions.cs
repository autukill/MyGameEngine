namespace GameEngine.Features.Presentation.Application;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;

public static class GameInstancePresentationExtensions
{
    public static void RequestPresentSurface(
        this GameInstance instance,
        RenderSurfaceKey source,
        Action<IDomainEvent> raiseEvent,
        int layer = 0,
        PresentationBlendMode blend = PresentationBlendMode.AlphaBlend,
        ViewportRect? viewport = null,
        ViewportFitMode fit = ViewportFitMode.Stretch)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        if (!instance.IsActive) return;
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            new PresentSurfaceDescriptor(
                PresentSurfaceDescriptor.DefaultKey,
                source,
                viewport ?? ViewportRect.FullScreen,
                layer,
                blend,
                fit)));
    }

    public static void ReleasePresentSurface(
        this GameInstance instance,
        Action<IDomainEvent> raiseEvent)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        raiseEvent(new RenderEffectReleasedEvent(
            instance.Id,
            PresentSurfaceDescriptor.DefaultKey));
    }
}
