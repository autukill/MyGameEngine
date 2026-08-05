namespace GameEngine.Core.Domain.Aggregates;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Domain.Entities;

/// <summary>
/// 场景聚合根（对应 GMS 的 Room）。
///
/// 职责：
///   1. 管理本场景内所有 GameInstance 的生命周期（Add / Destroy）
///   2. 调度 GMS 风格事件：OnCreate / OnStep / OnDestroy
///   3. 提供绘制入口 DrawActive(batch)：按 Depth 排序调用每个实例的 OnDraw
///   4. 收集未提交领域事件 UncommittedEvents
///
/// 注意：DrawActive 接受 ISpriteBatch 接口（而非 SpriteBatch 实现），
/// 保持本类虽在 Domain 层但只依赖领域抽象，不直接引用 Silk.NET / OpenGL。
/// </summary>
public class SceneAggregate
{
    public Guid SceneId { get; }
    public string SceneName { get; }
    public InstanceId AggregateId { get; }

    private readonly Dictionary<InstanceId, GameInstance> _instances = new();
    private readonly List<IDomainEvent> _uncommittedEvents = new();

    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<GameInstance> AllInstances => _instances.Values.ToList();
    public IEnumerable<GameInstance> ActiveInstances => _instances.Values.Where(i => i.IsActive);
    public int InstanceCount => _instances.Count;

    public SceneAggregate(string sceneName) : this(Guid.NewGuid(), sceneName) { }

    public SceneAggregate(Guid sceneId, string sceneName)
    {
        SceneId = sceneId;
        SceneName = sceneName;
        AggregateId = InstanceId.New();
    }

    // ============ GMS 风格：实例生命周期 ============

    /// <summary>
    /// GMS instance_create 等价：把一个已构造的 GameInstance 子类实例加入场景。
    /// 自动调用 OnCreate()（GMS Create 事件）。
    /// 用法：scene.Add(new PlayerSprite(pos));
    /// </summary>
    public T Add<T>(T instance) where T : GameInstance
    {
        _instances[instance.Id] = instance;
        instance.OnCreate();
        RaiseEvent(new InstanceSpawnedEvent(
            instance.Id, instance.ObjectTypeName,
            instance.Transform.Position, instance.Depth));
        return instance;
    }

    /// <summary>
    /// 兼容旧版：以基类 GameInstance 创建一个简单实例。
    /// 推荐用 Add(new YourSubclass(...)) 代替。
    /// </summary>
    public GameInstance Spawn(string objectTypeName, Vector2D position, LayerDepth depth)
    {
        var instance = new GameInstance(objectTypeName, position, depth);
        return Add(instance);
    }

    /// <summary>
    /// 销毁实例。触发 OnDestroy() + InstanceDestroyedEvent。
    /// </summary>
    public void Destroy(InstanceId id)
    {
        if (!_instances.TryGetValue(id, out var instance)) return;
        instance.OnDestroy();
        _instances.Remove(id);
        RaiseEvent(new InstanceDestroyedEvent(id, instance.ObjectTypeName));
    }

    // ============ GMS 风格：每帧调度 ============

    /// <summary>
    /// GMS Step 事件调度：遍历所有活跃实例调用 OnStep。
    /// 应在每帧 Update 阶段调用一次。
    /// </summary>
    public void PerformStep(double deltaTime)
    {
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.OnStep(deltaTime);
        }
    }

    /// <summary>
    /// GMS Draw 事件调度：按 Depth 排序后遍历活跃实例调用 OnDraw。
    /// 应在每帧 Render 阶段、SpriteBatch.Begin() 之后调用。
    /// </summary>
    public void DrawActive(ISpriteBatch batch)
    {
        // 按 Depth 值降序排（GMS: depth 大的先画，在底层）
        var sorted = _instances.Values
            .Where(i => i.IsActive)
            .OrderByDescending(i => i.Depth.Value);

        foreach (var instance in sorted)
        {
            instance.OnDraw(batch);
        }
    }

    // ============ 查询 ============

    public GameInstance? FindById(InstanceId id) =>
        _instances.TryGetValue(id, out var i) ? i : null;

    public IEnumerable<GameInstance> FindByType(string objectTypeName) =>
        _instances.Values.Where(i => i.ObjectTypeName == objectTypeName);

    /// <summary>按运行时类类型查找（GMS: instance_find(obj_player, 0)）</summary>
    public IEnumerable<T> FindByType<T>() where T : GameInstance =>
        _instances.Values.OfType<T>();

    // ============ 事件 ============

    public void RaiseEvent(IDomainEvent domainEvent) =>
        _uncommittedEvents.Add(domainEvent);

    public void MarkEventsAsCommitted() => _uncommittedEvents.Clear();

    /// <summary>重置场景：调用 OnDestroy + 移除所有非持久实例</summary>
    public void Reset()
    {
        var nonPersistent = _instances.Values.Where(i => !i.IsPersistent).ToList();
        foreach (var instance in nonPersistent)
        {
            instance.OnDestroy();
            _instances.Remove(instance.Id);
            RaiseEvent(new InstanceDestroyedEvent(instance.Id, instance.ObjectTypeName));
        }
    }
}
