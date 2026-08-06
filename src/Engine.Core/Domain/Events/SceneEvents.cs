namespace GameEngine.Core.Domain.Events;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 场景级领域事件。
///
/// 与实例级事件（InstanceSpawned/Moved/Destroyed）不同，
/// 这些事件描述场景本身的状态变迁，供编辑器/AI Agent/网络同步等外部源监听。
///
/// GMS 对照：
///   - SceneStarted <-> Room Start 事件
///   - SceneEnded   <-> Room End 事件
///   - LayerAdded   <-> GMS 无直接对应，为引擎扩展
/// </summary>

/// <summary>
/// 场景启动事件（场景切换进入时触发）。
/// 由 SceneAggregate 在首次 Step 前或显式 Start() 时发出。
/// </summary>
public sealed record SceneStartedEvent(
    Guid SceneId,
    string SceneName,
    int InstanceCount
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 场景结束事件（场景切换离开时触发）。
/// 由 SceneAggregate.End() 或 Reset() 时发出。
/// </summary>
public sealed record SceneEndedEvent(
    Guid SceneId,
    string SceneName
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 图层添加事件。编辑器/AI Agent 可监听以同步 Scene Tree 视图。
/// </summary>
public sealed record LayerAddedEvent(
    Guid SceneId,
    string LayerName,
    int DepthOrder
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

/// <summary>
/// 图层可见性变更事件。
/// </summary>
public sealed record LayerVisibilityChangedEvent(
    Guid SceneId,
    string LayerName,
    bool IsVisible
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
