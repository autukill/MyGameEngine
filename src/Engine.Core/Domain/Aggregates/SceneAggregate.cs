namespace GameEngine.Core.Domain.Aggregates;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Domain.Entities;

/// <summary>
/// 场景聚合根（对应 GMS 的 Room）。
///
/// 完整职责（Phase 1.4 统一后）：
///   1. Viewport 尺寸 — 场景的"世界坐标边界"
///   2. Layer 配置 — 图层名称/深度/可见性（领域元数据，不含渲染）
///   3. Background 配置 — 清屏色 + 背景精灵 + 平铺模式
///   4. GameInstance 生命周期 — Create/Step/Destroy（原有能力）
///   5. Scene 级 Hook — OnStart / OnEnd / OnBeforeStep / OnAfterStep
///   6. 领域事件收集 — 实例事件 + 场景事件
///
/// 约束（Scene & Instance 限界上下文）：
///   - 不直接调 OpenGL / Silk.NET
///   - 不计算空间碰撞（由 Physics 上下文负责）
///   - Camera2D 不归属 Scene（由渲染 Pass 持有注入）
///
/// 生命周期：
///   Start() -> [Step loop: OnBeforeStep -> PerformStep -> OnAfterStep] -> End()
/// </summary>
public class SceneAggregate
{
    // ============ 聚合根标识 ============

    public Guid SceneId { get; }
    public string SceneName { get; }
    public InstanceId AggregateId { get; }

    // ============ Viewport ============

    /// <summary>场景视口宽度（世界坐标像素）。默认 1280。</summary>
    public int ViewportWidth { get; set; } = 1280;

    /// <summary>场景视口高度（世界坐标像素）。默认 720。</summary>
    public int ViewportHeight { get; set; } = 720;

    // ============ Background ============

    /// <summary>背景配置（清屏色 + 可选精灵 + 平铺模式）。</summary>
    public BackgroundConfig Background { get; set; } = BackgroundConfig.EngineDefault;

    // ============ Layer 配置（领域层元数据） ============

    /// <summary>图层 GMS 预定义层名</summary>
    public const string LayerNameBackground = "Background";
    public const string LayerNameInstances = "Instances";
    public const string LayerNameUI = "UI";

    private readonly List<SceneLayerConfig> _layers = new();

    /// <summary>获取所有图层配置（按 DepthOrder 降序排列：值大的先渲染，位于底层）。</summary>
    public IReadOnlyList<SceneLayerConfig> Layers => _layers.AsReadOnly();

    // ============ Instance 存储（原有） ============

    private readonly Dictionary<InstanceId, GameInstance> _instances = new();
    private readonly List<IDomainEvent> _uncommittedEvents = new();
    private readonly IGameplayContext _gameplay;
    private List<PendingInstanceMutation> _pendingMutations = new();
    private List<PendingInstanceMutation> _committingMutations = new();
    private IInputProvider? _input;
    private ISpriteResolver? _sprites;

    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<GameInstance> AllInstances => _instances.Values.ToList();
    public IEnumerable<GameInstance> ActiveInstances => _instances.Values.Where(i => i.IsActive);
    public int InstanceCount => _instances.Count;

    // ============ Scene 级生命周期 Hook（委托） ============

    /// <summary>场景启动时调用（Start() 或首次 PerformStep 时触发）。</summary>
    public Action? OnStart { get; set; }

    /// <summary>场景结束时调用（End() 触发）。</summary>
    public Action? OnEnd { get; set; }

    /// <summary>每次 Step 之前调用（deltaTime 参数）。</summary>
    public Action<double>? OnBeforeStep { get; set; }

    /// <summary>每次 Step 之后调用（deltaTime 参数）。</summary>
    public Action<double>? OnAfterStep { get; set; }

    private bool _hasStarted = false;

    // ============ 构造函数 ============

    public SceneAggregate(string sceneName) : this(Guid.NewGuid(), sceneName) { }

    public SceneAggregate(Guid sceneId, string sceneName)
    {
        SceneId = sceneId;
        SceneName = sceneName;
        AggregateId = InstanceId.New();
        _gameplay = new SceneGameplayContext(this);

        // 默认创建 GMS 经典的三个图层
        AddLayer(LayerNameBackground, LayerDepth.Background.Value);
        AddLayer(LayerNameInstances, LayerDepth.Instances.Value);
        AddLayer(LayerNameUI, LayerDepth.UI.Value);
    }

    // ============ Layer 管理 ============

    /// <summary>
    /// 添加图层配置。自动按 DepthOrder 降序插入（值大的排在前面）。
    /// 如果同名 Layer 已存在，更新其 DepthOrder 和可见性。
    /// </summary>
    public SceneLayerConfig AddLayer(string name, int depthOrder, bool isVisible = true)
    {
        // 移除旧配置（如果存在）
        _layers.RemoveAll(l => l.Name == name);

        var config = new SceneLayerConfig(name, depthOrder, isVisible);

        // 按 DepthOrder 降序插入（值大的先渲染，在底层）
        int idx = 0;
        while (idx < _layers.Count && _layers[idx].DepthOrder > depthOrder)
            idx++;
        _layers.Insert(idx, config);

        RaiseEvent(new LayerAddedEvent(SceneId, name, depthOrder));
        return config;
    }

    /// <summary>移除图层（仅移除配置，不影响图层内的 Instance）。</summary>
    public bool RemoveLayer(string name)
    {
        return _layers.RemoveAll(l => l.Name == name) > 0;
    }

    /// <summary>设置图层可见性。</summary>
    public bool SetLayerVisible(string name, bool visible)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Name == name)
            {
                _layers[i] = _layers[i] with { IsVisible = visible };
                RaiseEvent(new LayerVisibilityChangedEvent(SceneId, name, visible));
                return true;
            }
        }
        return false;
    }

    /// <summary>根据名称查找图层配置。</summary>
    public SceneLayerConfig? FindLayerConfig(string name)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Name == name)
                return _layers[i];
        }
        return null;
    }

    // ============ Instance 生命周期（原有，增强 Layer 归属） ============

    /// <summary>
    /// GMS instance_create 等价：把 GameInstance 子类实例加入场景。
    /// 自动调用 OnCreate()，并如果 LayerName 为默认值则设为 "Instances"。
    /// </summary>
    public T Add<T>(T instance) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_instances.ContainsKey(instance.Id))
            throw new ArgumentException(
                $"Instance '{instance.Id}' is already in Scene '{SceneName}'.",
                nameof(instance));

        // 防御式：无 LayerName 的实例分配到 "Instances" 图层
        if (instance.LayerName == null)
            instance.LayerName = LayerNameInstances;

        instance.AttachGameplayContext(_gameplay);
        instance.Input ??= _input;
        instance.SpriteResolver ??= _sprites;
        _instances.Add(instance.Id, instance);
        try
        {
            instance.OnCreate();
        }
        catch
        {
            _instances.Remove(instance.Id);
            instance.DetachGameplayContext(_gameplay);
            throw;
        }
        RaiseEvent(new InstanceSpawnedEvent(
            instance.Id, instance.ObjectTypeName,
            instance.Transform.Position, instance.Depth));
        return instance;
    }

    /// <summary>
    /// 兼容旧版 Spawn：创建一个简单实例并分配图层。
    /// </summary>
    public GameInstance Spawn(string objectTypeName, Vector2D position, LayerDepth depth,
        string layerName = LayerNameInstances)
    {
        var instance = new GameInstance(objectTypeName, position, depth);
        instance.LayerName = layerName;
        return Add(instance);
    }

    /// <summary>销毁实例。触发 OnDestroy() + InstanceDestroyedEvent。</summary>
    public void Destroy(InstanceId id)
    {
        if (!_instances.TryGetValue(id, out var instance)) return;
        instance.OnDestroy();
        _instances.Remove(id);
        instance.DetachGameplayContext(_gameplay);
        RaiseEvent(new InstanceDestroyedEvent(id, instance.ObjectTypeName));
    }

    // ============ Scene 生命周期 ============

    /// <summary>
    /// 场景启动。发出 SceneStartedEvent，调用 OnStart Hook。
    /// 幂等：多次调用只触发一次。
    /// </summary>
    public void Start()
    {
        if (_hasStarted) return;
        _hasStarted = true;

        OnStart?.Invoke();
        RaiseEvent(new SceneStartedEvent(SceneId, SceneName, InstanceCount));
    }

    /// <summary>
    /// 场景结束。发出 SceneEndedEvent，调用 OnEnd Hook，重置非持久实例。
    /// </summary>
    public void End()
    {
        if (!_hasStarted) return;

        OnEnd?.Invoke();
        Reset();
        RaiseEvent(new SceneEndedEvent(SceneId, SceneName));
        _hasStarted = false;
    }

    // ============ 每帧调度 ============

    /// <summary>
    /// GMS Step 事件调度：触发 OnBeforeStep → 遍历实例 OnStep → OnAfterStep。
    /// 首次调用时自动触发 Start()（懒启动）。
    /// </summary>
    public void PerformStep(double deltaTime)
    {
        if (!_hasStarted) Start();

        OnBeforeStep?.Invoke(deltaTime);

        // Lightweight alarms fire before Begin Step. Inactive instances remain paused.
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.AdvanceAlarms(deltaTime);
        }

        // GMS Begin Step：所有活跃实例先执行（输入预处理/状态缓存）
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.OnBeginStep(deltaTime);
        }

        // GMS Step：主游戏逻辑
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.OnStep(deltaTime);
        }

        // GMS End Step：校验/后处理
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.OnEndStep(deltaTime);
        }

        // Sprite 动画在所有 End Step 完成后统一推进，Draw 阶段读取新帧。
        foreach (var instance in _instances.Values.ToList())
        {
            if (instance.IsActive)
                instance.AdvanceSpriteAnimation(deltaTime);
        }

        ApplyPendingMutations();

        OnAfterStep?.Invoke(deltaTime);
    }

    /// <summary>
    /// 输入沿事件分发（GMS Key Down / Key Up 事件）。
    /// 在 PerformStep 之前调用；keysPressed / keysReleased 为本帧按下/释放的键集合。
    /// </summary>
    public void PerformInput(IReadOnlyList<InputKey> keysPressed, IReadOnlyList<InputKey> keysReleased)
    {
        if (keysPressed.Count > 0)
        {
            foreach (var instance in _instances.Values.ToList())
            {
                if (!instance.IsActive) continue;
                for (int i = 0; i < keysPressed.Count; i++)
                    instance.OnKeyDown(keysPressed[i]);
            }
        }

        if (keysReleased.Count > 0)
        {
            foreach (var instance in _instances.Values.ToList())
            {
                if (!instance.IsActive) continue;
                for (int i = 0; i < keysReleased.Count; i++)
                    instance.OnKeyUp(keysReleased[i]);
            }
        }
    }

    /// <summary>设置场景共享输入提供者（对已有实例补注入；之后 Add 的实例自动注入）</summary>
    public void SetInput(IInputProvider? input)
    {
        _input = input;
        foreach (var instance in _instances.Values)
            instance.Input ??= input;
    }

    /// <summary>设置场景共享 Sprite 解析器（对已有实例补注入；之后 Add 自动注入）。</summary>
    public void SetSprites(ISpriteResolver? sprites)
    {
        _sprites = sprites;
        foreach (var instance in _instances.Values)
            instance.SpriteResolver ??= sprites;
    }

    /// <summary>
    /// GMS Draw 事件调度（Layer 感知版）。
    ///
    /// 按 Layer 分组绘制：先遍历 Layer 配置（跳过 IsVisible=false），
    /// 再遍历该 Layer 下所有活跃实例，按 Depth 降序排序后调用 OnDraw。
    ///
    /// 渲染约定：
    ///   - 调用方在调用前须已调用 SpriteBatch.Begin()
    ///   - 调用方在调用后须调用 SpriteBatch.End()
    ///   - 各 Layer 间的 GL 状态切换（Blend/Stencil）由 SceneRenderPass 在层间插入
    /// </summary>
    public void DrawActive(ISpriteBatch batch)
    {
        foreach (var layer in _layers)
        {
            if (!layer.IsVisible) continue;

            var layerInstances = _instances.Values
                .Where(i => i.IsActive && i.LayerName == layer.Name);

            // 同 Layer 内按 Depth 降序排序（Depth 值大的先画，在底层）
            var sorted = layerInstances.OrderByDescending(i => i.Depth.Value);

            foreach (var instance in sorted)
            {
                ApplyRenderState(batch, instance);
                instance.OnBeginDraw(batch);
                instance.OnDraw(batch);
                instance.OnEndDraw(batch);
            }
        }
    }

    /// <summary>
    /// 应用实例级渲染状态（RenderStyle/Shader）。
    /// SpriteBatch 内部对未变化的状态零开销，变化时自动 Flush + Apply。
    /// </summary>
    private static void ApplyRenderState(ISpriteBatch batch, GameInstance instance)
    {
        batch.SetBlendMode(instance.RenderStyle.BlendMode);
        batch.SetDepthState(instance.RenderStyle.DepthTest, instance.RenderStyle.DepthWrite);
        if (instance.Material is { IsEmpty: false } material)
            batch.SetMaterial(material);
        else
            batch.SetShader(instance.Shader);
    }

    /// <summary>
    /// 单 Layer 渲染：只绘制指定图层的活跃实例。
    /// 用于 StencilMaskPass 等只需重绘特定层的场景。
    /// </summary>
    public void DrawActive(ISpriteBatch batch, string layerName)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            if (layer.Name != layerName || !layer.IsVisible) continue;

            var layerInstances = _instances.Values
                .Where(i => i.IsActive && i.LayerName == layer.Name);

            var sorted = layerInstances.OrderByDescending(i => i.Depth.Value);

            foreach (var instance in sorted)
            {
                ApplyRenderState(batch, instance);
                instance.OnBeginDraw(batch);
                instance.OnDraw(batch);
                instance.OnEndDraw(batch);
            }
            return; // 只绘制匹配的第一个 Layer
        }
    }

    /// <summary>
    /// GMS Draw GUI 事件调度：所有活跃实例的屏幕空间 UI 绘制（不受相机影响）。
    /// 调用方需自行 Begin/End SpriteBatch。
    /// </summary>
    public void DrawGUI(ISpriteBatch batch)
    {
        foreach (var instance in _instances.Values)
        {
            if (!instance.IsActive) continue;
            instance.OnDrawGUI(batch);
        }
    }

    // ============ 查询（原有） ============

    public GameInstance? FindById(InstanceId id) =>
        _instances.TryGetValue(id, out var i) ? i : null;

    public IEnumerable<GameInstance> FindByType(string objectTypeName) =>
        _instances.Values.Where(i => i.ObjectTypeName == objectTypeName);

    /// <summary>按运行时类型查找（GMS: instance_find）。</summary>
    public IEnumerable<T> FindByType<T>() where T : GameInstance =>
        _instances.Values.OfType<T>();

    /// <summary>按图层名获取所有活跃实例。</summary>
    public IEnumerable<GameInstance> GetInstancesInLayer(string layerName) =>
        _instances.Values.Where(i => i.IsActive && i.LayerName == layerName);

    // ============ 事件（原有） ============

    public void RaiseEvent(IDomainEvent domainEvent) =>
        _uncommittedEvents.Add(domainEvent);

    public void MarkEventsAsCommitted() => _uncommittedEvents.Clear();

    /// <summary>获取稳定事件快照并清空列表；同一快照可依次交给多个消费者。</summary>
    public IReadOnlyList<IDomainEvent> DrainUncommittedEvents()
    {
        if (_uncommittedEvents.Count == 0)
            return Array.Empty<IDomainEvent>();
        var snapshot = _uncommittedEvents.ToArray();
        _uncommittedEvents.Clear();
        return snapshot;
    }

    /// <summary>重置场景：调用 OnDestroy + 移除所有非持久实例。</summary>
    public void Reset()
    {
        ClearPendingMutations();
        var nonPersistent = _instances.Values.Where(i => !i.IsPersistent).ToList();
        foreach (var instance in nonPersistent)
        {
            instance.OnDestroy();
            _instances.Remove(instance.Id);
            instance.DetachGameplayContext(_gameplay);
            RaiseEvent(new InstanceDestroyedEvent(instance.Id, instance.ObjectTypeName));
        }
        ClearPendingMutations();
    }

    private T QueueSpawn<T>(T instance) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_instances.ContainsKey(instance.Id) ||
            _pendingMutations.Any(mutation =>
                mutation.Kind == InstanceMutationKind.Spawn &&
                mutation.Instance?.Id == instance.Id))
        {
            throw new ArgumentException(
                $"Instance '{instance.Id}' is already added or queued for Scene '{SceneName}'.",
                nameof(instance));
        }

        instance.AttachGameplayContext(_gameplay);
        _pendingMutations.Add(PendingInstanceMutation.Spawn(instance));
        return instance;
    }

    private void QueueDestroy(InstanceId id) =>
        _pendingMutations.Add(PendingInstanceMutation.Destroy(id));

    private void ApplyPendingMutations()
    {
        if (_pendingMutations.Count == 0) return;
        (_pendingMutations, _committingMutations) =
            (_committingMutations, _pendingMutations);
        try
        {
            for (int i = 0; i < _committingMutations.Count; i++)
            {
                PendingInstanceMutation mutation = _committingMutations[i];
                if (mutation.Kind == InstanceMutationKind.Spawn)
                    Add(mutation.Instance!);
                else
                    Destroy(mutation.InstanceId);
            }
        }
        catch
        {
            DetachQueuedSpawns(_committingMutations);
            throw;
        }
        finally
        {
            _committingMutations.Clear();
        }
    }

    private void ClearPendingMutations()
    {
        DetachQueuedSpawns(_pendingMutations);
        DetachQueuedSpawns(_committingMutations);
        _pendingMutations.Clear();
        _committingMutations.Clear();
    }

    private void DetachQueuedSpawns(IReadOnlyList<PendingInstanceMutation> mutations)
    {
        for (int i = 0; i < mutations.Count; i++)
        {
            GameInstance? instance = mutations[i].Instance;
            if (mutations[i].Kind == InstanceMutationKind.Spawn && instance is not null &&
                !_instances.ContainsKey(instance.Id))
            {
                instance.DetachGameplayContext(_gameplay);
            }
        }
    }

    private enum InstanceMutationKind
    {
        Spawn,
        Destroy
    }

    private readonly record struct PendingInstanceMutation(
        InstanceMutationKind Kind,
        GameInstance? Instance,
        InstanceId InstanceId)
    {
        public static PendingInstanceMutation Spawn(GameInstance instance) =>
            new(InstanceMutationKind.Spawn, instance, instance.Id);

        public static PendingInstanceMutation Destroy(InstanceId id) =>
            new(InstanceMutationKind.Destroy, null, id);
    }

    private sealed class SceneGameplayContext(SceneAggregate owner) : IGameplayContext
    {
        public T Spawn<T>(T instance) where T : GameInstance => owner.QueueSpawn(instance);

        public void Destroy(InstanceId id) => owner.QueueDestroy(id);

        public GameInstance? FindById(InstanceId id) => owner.FindById(id);

        public T? FindFirst<T>() where T : GameInstance =>
            owner._instances.Values.OfType<T>().FirstOrDefault();

        public IReadOnlyList<T> FindAll<T>() where T : GameInstance =>
            owner._instances.Values.OfType<T>().ToArray();
    }
}
