namespace GameEngine.Features.StencilMasking.Application;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.StencilMasking.Domain;

public static class GameInstanceStencilExtensions
{
    public static void RequestStencilMask(
        this GameInstance instance,
        Vector2D center,
        float radius,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        if (!instance.IsActive) return;
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            new StencilMaskEffectDescriptor(
                key ?? StencilMaskEffectDescriptor.DefaultKey,
                center,
                radius,
                state)));
    }

    public static void ReleaseStencilMask(
        this GameInstance instance,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        raiseEvent(new RenderEffectReleasedEvent(
            instance.Id,
            key ?? StencilMaskEffectDescriptor.DefaultKey));
    }

    public static void RequestStencilSpriteMask(
        this GameInstance instance,
        SpriteRef sprite,
        float subImage,
        Transform2D transform,
        float alphaCutoff,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        if (!instance.IsActive) return;
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            new StencilMaskEffectDescriptor(
                key ?? StencilMaskEffectDescriptor.DefaultKey,
                StencilMaskGeometry.FromSprite(sprite, subImage, transform, alphaCutoff),
                state)));
    }
}
