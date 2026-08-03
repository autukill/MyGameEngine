namespace GameEngine.Core.Domain.Aggregates;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;

public class SceneAggregate
{
    public Guid SceneId { get; }
    public string SceneName { get; private set; }

    private readonly Dictionary<InstanceId, GameInstance> _instances = new();
    private readonly List<IDomainEvent> _uncommittedEvents = new();

    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<GameInstance> ActiveInstances => _instances.Values.Where(i => i.IsActive).ToList();

    public SceneAggregate(Guid sceneId, string sceneName)
    {
        SceneId = sceneId;
        SceneName = sceneName;
    }

    /// <summary>
    /// 生成新实例（维持聚合内部一致性）
    /// </summary>
    public GameInstance Spawn(string objectTypeName, Vector2D position, LayerDepth depth)
    {
        var id = InstanceId.New();
        var transform = Transform2D.Default with { Position = position };
        var instance = new GameInstance(id, objectTypeName, transform, depth);

        _instances.Add(id, instance);

        // 记录领域事件
        RaiseEvent(new InstanceSpawnedEvent(id, objectTypeName, position, depth));

        return instance;
    }

    /// <summary>
    /// 执行一帧的物理与逻辑 Step 循环
    /// </summary>
    public void PerformStep(Action<GameInstance> stepLogic)
    {
        foreach (var instance in _instances.Values.Where(i => i.IsActive))
        {
            stepLogic(instance);
        }
    }

    public void RaiseEvent(IDomainEvent domainEvent)
    {
        _uncommittedEvents.Add(domainEvent);
    }

    public void ClearEvents()
    {
        _uncommittedEvents.Clear();
    }
}
