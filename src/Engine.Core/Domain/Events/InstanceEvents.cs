namespace GameEngine.Core.Domain.Events;

using System;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 实例被生成到场景中时触发。
/// 由 SceneAggregate.Spawn() 发出，物理切片可捕获以更新空间索引。
/// </summary>
public sealed record InstanceSpawnedEvent(
    InstanceId Id,
    string ObjectTypeName,
    Vector2D Position,
    LayerDepth Depth
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 实例移动时触发。
/// 由 GameInstance.MoveTo() 发出，物理切片捕获后增量更新 Spatial Hash 桶。
/// </summary>
public sealed record InstanceMovedEvent(
    InstanceId Id,
    Vector2D OldPosition,
    Vector2D NewPosition
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 实例被销毁时触发。
/// 由 SceneAggregate.Destroy() 发出，物理切片捕获后从空间索引中移除。
/// </summary>
public sealed record InstanceDestroyedEvent(
    InstanceId Id,
    string ObjectTypeName
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 实例状态变更（激活/停用）。
/// 仅在 IsActive 切换时触发，渲染切片捕获后控制是否参与绘制。
/// </summary>
public sealed record InstanceActivationChangedEvent(
    InstanceId Id,
    bool IsActive
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
