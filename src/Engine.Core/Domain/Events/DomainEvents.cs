namespace GameEngine.Core.Domain.Events;

using GameEngine.Core.Domain.ValueObjects;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}

/// <summary>
/// 实例生成事件
/// </summary>
public record InstanceSpawnedEvent(InstanceId Id, string ObjectType, Vector2D Position, LayerDepth Depth) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 实例移动事件（告知物理系统更新空间 Hash）
/// </summary>
public record InstanceMovedEvent(InstanceId Id, Vector2D OldPosition, Vector2D NewPosition) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 关键突破：请求模板遮罩渲染 Pass 事件（解耦 GameMaker 无法操作 GL_STENCIL_TEST 的痛点）
/// </summary>
public record StencilMaskPassRequestedEvent(
    InstanceId ProviderId,
    Action RenderMaskShape,
    Action RenderMaskedContent
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
