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
        StencilMaskGroupRef group,
        Vector2D center,
        float radius,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent)
    {
        if (!CanRequest(instance, raiseEvent)) return;
        RaiseRequest(instance, new StencilMaskEffectDescriptor(
            group.Key,
            center,
            radius,
            state), raiseEvent);
    }

    public static void RequestStencilMask(
        this GameInstance instance,
        Vector2D center,
        float radius,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        if (!CanRequest(instance, raiseEvent)) return;
        RaiseRequest(instance, new StencilMaskEffectDescriptor(
            key ?? StencilMaskEffectDescriptor.DefaultKey,
            center,
            radius,
            state), raiseEvent);
    }

    public static void RequestStencilMasks(
        this GameInstance instance,
        StencilMaskGroupRef group,
        ReadOnlySpan<StencilMaskGeometry> geometries,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent)
    {
        if (!CanRequest(instance, raiseEvent)) return;
        RaiseRequest(instance, new StencilMaskEffectDescriptor(
            group.Key,
            geometries,
            state), raiseEvent);
    }

    public static void ReleaseStencilMask(
        this GameInstance instance,
        StencilMaskGroupRef group,
        Action<IDomainEvent> raiseEvent) =>
        Release(instance, group.Key, raiseEvent);

    public static void ReleaseStencilMask(
        this GameInstance instance,
        Action<IDomainEvent> raiseEvent,
        RenderEffectKey? key = null)
    {
        Release(instance, key ?? StencilMaskEffectDescriptor.DefaultKey, raiseEvent);
    }

    public static void RequestStencilSpriteMask(
        this GameInstance instance,
        StencilMaskGroupRef group,
        SpriteRef sprite,
        float subImage,
        Transform2D transform,
        float alphaCutoff,
        StencilMaskState state,
        Action<IDomainEvent> raiseEvent)
    {
        if (!CanRequest(instance, raiseEvent)) return;
        RaiseRequest(instance, new StencilMaskEffectDescriptor(
            group.Key,
            StencilMaskGeometry.FromSprite(sprite, subImage, transform, alphaCutoff),
            state), raiseEvent);
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
        if (!CanRequest(instance, raiseEvent)) return;
        RaiseRequest(instance, new StencilMaskEffectDescriptor(
            key ?? StencilMaskEffectDescriptor.DefaultKey,
            StencilMaskGeometry.FromSprite(sprite, subImage, transform, alphaCutoff),
            state), raiseEvent);
    }

    private static bool CanRequest(
        GameInstance instance,
        Action<IDomainEvent> raiseEvent)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        return instance.IsActive;
    }

    private static void RaiseRequest(
        GameInstance instance,
        StencilMaskEffectDescriptor descriptor,
        Action<IDomainEvent> raiseEvent)
    {
        raiseEvent(new RenderEffectRequestedEvent(
            instance.Id,
            descriptor));
    }

    private static void Release(
        GameInstance instance,
        RenderEffectKey key,
        Action<IDomainEvent> raiseEvent)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(raiseEvent);
        raiseEvent(new RenderEffectReleasedEvent(instance.Id, key));
    }
}
