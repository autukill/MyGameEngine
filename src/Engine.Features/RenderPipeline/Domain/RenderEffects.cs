namespace GameEngine.Features.RenderPipeline.Domain;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>逻辑渲染效果标识。Kind 选择工厂，Slot 区分同类效果的共享实例。</summary>
public readonly record struct RenderEffectKey
{
    public string Kind { get; }
    public string Slot { get; }

    public RenderEffectKey(string kind, string slot)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Effect kind cannot be empty.", nameof(kind));
        if (string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("Effect slot cannot be empty.", nameof(slot));
        Kind = kind;
        Slot = slot;
    }

    public override string ToString() => $"{Kind}:{Slot}";
}

/// <summary>领域层效果描述符；实现不得携带 GL、Shader、Pass 或绘制回调。</summary>
public interface IRenderEffectDescriptor
{
    RenderEffectKey Key { get; }
}

public sealed record RenderEffectRequestedEvent(
    InstanceId OwnerId,
    IRenderEffectDescriptor Descriptor) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record RenderEffectReleasedEvent(
    InstanceId OwnerId,
    RenderEffectKey EffectKey) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
