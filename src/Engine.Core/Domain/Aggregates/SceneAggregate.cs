namespace GameEngine.Core.Domain.Aggregates;

using System.Diagnostics;
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
public class SceneAggregate : IInstanceDrawTracker
{
    // ============ 聚合根标识 ============

    public Guid SceneId { get; }
    public string SceneName { get; private set; }
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
    private readonly Dictionary<InstanceId, IndexedInstance> _indexedInstances = new();
    private readonly Dictionary<string, List<IndexedInstance>> _instancesByLayer =
        new(StringComparer.Ordinal);
    private readonly List<IDomainEvent> _uncommittedEvents = new();
    private readonly List<GameInstance> _lifecycleSnapshot = new();
    private readonly List<GameInstance> _guiSnapshot = new();
    private readonly List<DrawEntry> _drawSnapshot = new();
    private readonly QueryCounters[] _queryCounters = new QueryCounters[4];
    private readonly IGameplayContext _gameplay;
    private List<PendingInstanceMutation> _pendingMutations = new();
    private List<PendingInstanceMutation> _committingMutations = new();
    private IInputProvider? _input;
    private InputMap? _inputMap;
    private ISpriteResolver? _sprites;
    private IInstanceFactory _instanceFactory = new InstanceFactory().Build();
    private ISceneSwitchRequester? _sceneSwitchRequester;
    private bool _gameplayQueryStatisticsEnabled;
    private long _querySampledSteps;
    private long _nextDrawSequence;

    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();
    public IReadOnlyCollection<GameInstance> AllInstances => _instances.Values.ToList();
    public IEnumerable<GameInstance> ActiveInstances => _instances.Values.Where(i => i.IsActive);
    public int InstanceCount => _instances.Count;
    public GameplayTimeController Time { get; } = new();
    public bool GameplayQueryStatisticsEnabled => _gameplayQueryStatisticsEnabled;

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
        instance.MappedInput ??= _inputMap;
        instance.SpriteResolver ??= _sprites;
        _instances.Add(instance.Id, instance);
        try
        {
            instance.AttachDrawTracker(this);
            IndexInstance(instance);
            instance.OnCreate();
        }
        catch
        {
            instance.DetachDrawTracker(this);
            UnindexInstance(instance.Id);
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
        Time.ReleaseOwner(id);
        instance.DetachDrawTracker(this);
        UnindexInstance(id);
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

    /// <summary>
    /// Ends the current logical Scene, preserves persistent instances, resets Scene-local
    /// configuration, and assigns the next registered Scene name. The caller configures and starts
    /// the new definition after this method returns.
    /// </summary>
    public void TransitionTo(string sceneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);
        if (_hasStarted)
            End();
        else
            Reset();

        SceneName = sceneName;
        Background = BackgroundConfig.EngineDefault;
        OnStart = null;
        OnEnd = null;
        OnBeforeStep = null;
        OnAfterStep = null;
        _layers.Clear();
        AddLayer(LayerNameBackground, LayerDepth.Background.Value);
        AddLayer(LayerNameInstances, LayerDepth.Instances.Value);
        AddLayer(LayerNameUI, LayerDepth.UI.Value);
    }

    // ============ 每帧调度 ============

    /// <summary>
    /// GMS Step 事件调度：触发 OnBeforeStep → 遍历实例 OnStep → OnAfterStep。
    /// 首次调用时自动触发 Start()（懒启动）。
    /// </summary>
    public void PerformStep(double deltaTime)
    {
        if (!_hasStarted) Start();
        GameplayTimeSnapshot time = Time.BeginFrame(deltaTime);

        if (!time.IsPaused)
            OnBeforeStep?.Invoke(time.DeltaTime);

        // Lightweight alarms fire before Begin Step in each Instance's selected time domain.
        CaptureInstances(_lifecycleSnapshot);
        foreach (var instance in _lifecycleSnapshot)
        {
            if (ShouldUpdate(instance, time))
                instance.AdvanceAlarms(DeltaFor(instance, time));
        }

        // GMS Begin Step：所有活跃实例先执行（输入预处理/状态缓存）
        CaptureInstances(_lifecycleSnapshot);
        foreach (var instance in _lifecycleSnapshot)
        {
            if (ShouldUpdate(instance, time))
                instance.OnBeginStep(DeltaFor(instance, time));
        }

        // GMS Step：主游戏逻辑
        CaptureInstances(_lifecycleSnapshot);
        foreach (var instance in _lifecycleSnapshot)
        {
            if (ShouldUpdate(instance, time))
                instance.OnStep(DeltaFor(instance, time));
        }

        // GMS End Step：校验/后处理
        CaptureInstances(_lifecycleSnapshot);
        foreach (var instance in _lifecycleSnapshot)
        {
            if (ShouldUpdate(instance, time))
                instance.OnEndStep(DeltaFor(instance, time));
        }

        // Sprite 动画在所有 End Step 完成后统一推进，Draw 阶段读取新帧。
        CaptureInstances(_lifecycleSnapshot);
        foreach (var instance in _lifecycleSnapshot)
        {
            if (ShouldUpdate(instance, time))
                instance.AdvanceSpriteAnimation(DeltaFor(instance, time));
        }

        ApplyPendingMutations();

        if (!time.IsPaused)
            OnAfterStep?.Invoke(time.DeltaTime);

        if (_gameplayQueryStatisticsEnabled)
            _querySampledSteps++;
    }

    /// <summary>
    /// 输入沿事件分发（GMS Key Down / Key Up 事件）。
    /// 在 PerformStep 之前调用；keysPressed / keysReleased 为本帧按下/释放的键集合。
    /// </summary>
    public void PerformInput(IReadOnlyList<InputKey> keysPressed, IReadOnlyList<InputKey> keysReleased)
    {
        if (keysPressed.Count > 0)
        {
            CaptureInstances(_lifecycleSnapshot);
            foreach (var instance in _lifecycleSnapshot)
            {
                if (!CanReceiveInput(instance)) continue;
                for (int i = 0; i < keysPressed.Count; i++)
                    instance.OnKeyDown(keysPressed[i]);
            }
        }

        if (keysReleased.Count > 0)
        {
            CaptureInstances(_lifecycleSnapshot);
            foreach (var instance in _lifecycleSnapshot)
            {
                if (!CanReceiveInput(instance)) continue;
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

    /// <summary>Sets the shared immutable logical input map for existing and future instances.</summary>
    public void SetInputMap(InputMap? inputMap)
    {
        _inputMap = inputMap;
        foreach (var instance in _instances.Values)
            instance.MappedInput ??= inputMap;
    }

    /// <summary>设置场景共享 Sprite 解析器（对已有实例补注入；之后 Add 自动注入）。</summary>
    public void SetSprites(ISpriteResolver? sprites)
    {
        _sprites = sprites;
        foreach (var instance in _instances.Values)
            instance.SpriteResolver ??= sprites;
    }

    public void SetInstanceFactory(IInstanceFactory instanceFactory) =>
        _instanceFactory = instanceFactory ?? throw new ArgumentNullException(nameof(instanceFactory));

    /// <summary>Sets the Hosting-owned safe-boundary Scene switch requester.</summary>
    public void SetSceneSwitchRequester(ISceneSwitchRequester? requester) =>
        _sceneSwitchRequester = requester;

    /// <summary>
    /// Compatibility adapter for advanced untyped composition roots. Typed Scene requests require
    /// an ISceneSwitchRequester implementation.
    /// </summary>
    public void SetSceneSwitchRequester(Action<SceneRef>? requester) =>
        _sceneSwitchRequester = requester is null ? null : new UntypedSceneSwitchRequester(requester);

    /// <summary>
    /// GMS Draw 事件调度（Layer 感知版）。
    ///
    /// 按 Layer 分组绘制：先遍历 Layer 配置（跳过 IsVisible=false），
    /// 再遍历该 Layer 下所有活跃实例；内部索引已按 Depth 降序和加入顺序维护。
    ///
    /// 渲染约定：
    ///   - 调用方在调用前须已调用 SpriteBatch.Begin()
    ///   - 调用方在调用后须调用 SpriteBatch.End()
    ///   - 各 Layer 间的 GL 状态切换（Blend/Stencil）由 SceneRenderPass 在层间插入
    /// </summary>
    public void DrawActive(ISpriteBatch batch) => DrawActive(batch, SceneLayerFilter.All);

    /// <summary>
    /// Draws active instances from visible layers accepted by an immutable view filter.
    /// The filter performs no allocations during traversal.
    /// </summary>
    public void DrawActive(ISpriteBatch batch, SceneLayerFilter layerFilter)
    {
        _ = DrawActiveCore(batch, layerFilter, viewBounds: null, measureTime: false);
    }

    /// <summary>
    /// Draws active instances while conservatively rejecting known visual bounds outside a View.
    /// Instances without known bounds remain visible.
    /// </summary>
    public void DrawActive(
        ISpriteBatch batch,
        SceneLayerFilter layerFilter,
        Bounds2D viewBounds)
    {
        _ = DrawActiveCore(batch, layerFilter, viewBounds, measureTime: false);
    }

    /// <summary>
    /// Draws the same Scene path while returning exact traversal, sorting, and callback metrics.
    /// No collections are allocated; callers can sample every frame when diagnostics are enabled.
    /// </summary>
    public SceneDrawStatistics DrawActiveMeasured(
        ISpriteBatch batch,
        SceneLayerFilter layerFilter,
        bool measureTime = true) =>
        DrawActiveCore(batch, layerFilter, viewBounds: null, measureTime);

    /// <summary>Measured counterpart of the bounds-aware draw path.</summary>
    public SceneDrawStatistics DrawActiveMeasured(
        ISpriteBatch batch,
        SceneLayerFilter layerFilter,
        Bounds2D viewBounds,
        bool measureTime = true) =>
        DrawActiveCore(batch, layerFilter, viewBounds, measureTime);

    private SceneDrawStatistics DrawActiveCore(
        ISpriteBatch batch,
        SceneLayerFilter layerFilter,
        Bounds2D? viewBounds,
        bool measureTime)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(layerFilter);
        int visibleLayers = 0;
        int candidateVisits = 0;
        int culledInstances = 0;
        int selectedInstances = 0;
        int drawnInstances = 0;
        long traversalTicks = 0;
        long drawTicks = 0;
        foreach (var layer in _layers)
        {
            if (!layer.IsVisible || !layerFilter.Allows(layer.Name)) continue;

            visibleLayers++;
            long started = measureTime ? Stopwatch.GetTimestamp() : 0L;
            candidateVisits += CaptureDrawEntries(
                batch,
                layer.Name,
                viewBounds,
                out int layerCulled);
            culledInstances += layerCulled;
            if (measureTime) traversalTicks += Stopwatch.GetTimestamp() - started;

            selectedInstances += _drawSnapshot.Count;

            started = measureTime ? Stopwatch.GetTimestamp() : 0L;
            foreach (DrawEntry entry in _drawSnapshot)
            {
                GameInstance instance = entry.Instance;
                ApplyRenderState(batch, instance);
                instance.OnBeginDraw(batch);
                instance.OnDraw(batch);
                instance.OnEndDraw(batch);
                drawnInstances++;
            }
            if (measureTime) drawTicks += Stopwatch.GetTimestamp() - started;
        }

        return new SceneDrawStatistics(
            measureTime,
            visibleLayers,
            candidateVisits,
            culledInstances,
            selectedInstances,
            drawnInstances,
            SortComparisonCount: 0,
            TraversalTime: ToElapsedTime(traversalTicks),
            SortTime: TimeSpan.Zero,
            DrawTime: ToElapsedTime(drawTicks));
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

            CaptureDrawEntries(batch, layer.Name, viewBounds: null, out _);
            foreach (DrawEntry entry in _drawSnapshot)
            {
                GameInstance instance = entry.Instance;
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
        CaptureInstances(_guiSnapshot);
        foreach (var instance in _guiSnapshot)
        {
            if (!instance.IsActive) continue;
            instance.OnDrawGUI(batch);
        }
    }

    /// <summary>
    /// Enables low-overhead query timing and counters. Changing the setting resets accumulated
    /// measurements; Scene gameplay remains single-threaded.
    /// </summary>
    public void SetGameplayQueryStatisticsEnabled(bool enabled)
    {
        if (_gameplayQueryStatisticsEnabled == enabled) return;
        _gameplayQueryStatisticsEnabled = enabled;
        ResetGameplayQueryStatistics();
    }

    /// <summary>Captures query measurements accumulated since enable or the last reset.</summary>
    public GameplayQueryStatisticsSnapshot CaptureGameplayQueryStatistics(bool reset = false)
    {
        var snapshot = new GameplayQueryStatisticsSnapshot(
            _gameplayQueryStatisticsEnabled,
            _querySampledSteps,
            CaptureQueryMetric(QueryKind.Find),
            CaptureQueryMetric(QueryKind.Collision),
            CaptureQueryMetric(QueryKind.Area),
            CaptureQueryMetric(QueryKind.Radius));
        if (reset)
            ResetGameplayQueryStatistics();
        return snapshot;
    }

    // ============ 查询（原有） ============

    public GameInstance? FindById(InstanceId id) =>
        _instances.TryGetValue(id, out var i) ? i : null;

    public IEnumerable<GameInstance> FindByType(string objectTypeName) =>
        _instances.Values.Where(i => i.ObjectTypeName == objectTypeName);

    /// <summary>按运行时类型查找（GMS: instance_find）。</summary>
    public IEnumerable<T> FindByType<T>() where T : GameInstance =>
        _instances.Values.OfType<T>();

    public T? FindFirst<T>() where T : GameInstance
    {
        long started = BeginQuery();
        int candidates = 0;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate is not T typed) continue;
            RecordQuery(QueryKind.Find, started, candidates, 1);
            return typed;
        }
        RecordQuery(QueryKind.Find, started, candidates, 0);
        return null;
    }

    public IReadOnlyList<T> FindAll<T>() where T : GameInstance
    {
        long started = BeginQuery();
        int candidates = 0;
        List<T>? matches = null;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate is T typed)
                (matches ??= new List<T>()).Add(typed);
        }
        T[] result = matches?.ToArray() ?? Array.Empty<T>();
        RecordQuery(QueryKind.Find, started, candidates, result.Length);
        return result;
    }

    public int FindAll<T>(GameplayQueryBuffer<T> results) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        long started = BeginQuery();
        int candidates = 0;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate is T typed)
                results.Add(typed);
        }
        RecordQuery(QueryKind.Find, started, candidates, results.Count);
        return results.Count;
    }

    public int CountInstances<T>() where T : GameInstance
    {
        long started = BeginQuery();
        int candidates = 0;
        int count = 0;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate is T) count++;
        }
        RecordQuery(QueryKind.Find, started, candidates, count);
        return count;
    }

    public T? FirstCollision<T>(GameInstance source) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(source);
        long started = BeginQuery();
        int candidates = 0;
        if (source.Collider is not { } sourceShape)
        {
            RecordQuery(QueryKind.Collision, started, candidates, 0);
            return null;
        }
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate.Id == source.Id || !candidate.IsActive ||
                candidate is not T typed || candidate.Collider is not { } candidateShape)
            {
                continue;
            }
            if (CollisionMath2D.Intersects(
                    sourceShape,
                    source.Transform,
                    candidateShape,
                    candidate.Transform))
            {
                RecordQuery(QueryKind.Collision, started, candidates, 1);
                return typed;
            }
        }
        RecordQuery(QueryKind.Collision, started, candidates, 0);
        return null;
    }

    public IReadOnlyList<T> Collisions<T>(GameInstance source) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(source);
        long started = BeginQuery();
        int candidates = 0;
        if (source.Collider is not { } sourceShape)
        {
            RecordQuery(QueryKind.Collision, started, candidates, 0);
            return Array.Empty<T>();
        }
        List<T>? matches = null;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate.Id == source.Id || !candidate.IsActive ||
                candidate is not T typed || candidate.Collider is not { } candidateShape)
            {
                continue;
            }
            if (CollisionMath2D.Intersects(
                    sourceShape,
                    source.Transform,
                    candidateShape,
                    candidate.Transform))
            {
                (matches ??= new List<T>()).Add(typed);
            }
        }
        T[] result = matches?.ToArray() ?? Array.Empty<T>();
        RecordQuery(QueryKind.Collision, started, candidates, result.Length);
        return result;
    }

    public int Collisions<T>(GameInstance source, GameplayQueryBuffer<T> results)
        where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        long started = BeginQuery();
        int candidates = 0;
        if (source.Collider is not { } sourceShape)
        {
            RecordQuery(QueryKind.Collision, started, candidates, 0);
            return 0;
        }
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (candidate.Id == source.Id || !candidate.IsActive ||
                candidate is not T typed || candidate.Collider is not { } candidateShape)
            {
                continue;
            }
            if (CollisionMath2D.Intersects(
                    sourceShape,
                    source.Transform,
                    candidateShape,
                    candidate.Transform))
            {
                results.Add(typed);
            }
        }
        RecordQuery(QueryKind.Collision, started, candidates, results.Count);
        return results.Count;
    }

    public IReadOnlyList<T> QueryArea<T>(Bounds2D bounds) where T : GameInstance
    {
        long started = BeginQuery();
        int candidates = 0;
        List<T>? matches = null;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (!candidate.IsActive || candidate is not T typed ||
                candidate.Collider is not { } shape)
            {
                continue;
            }
            if (bounds.Intersects(CollisionMath2D.GetBounds(shape, candidate.Transform)))
                (matches ??= new List<T>()).Add(typed);
        }
        T[] result = matches?.ToArray() ?? Array.Empty<T>();
        RecordQuery(QueryKind.Area, started, candidates, result.Length);
        return result;
    }

    public int QueryArea<T>(Bounds2D bounds, GameplayQueryBuffer<T> results)
        where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(results);
        results.Clear();
        long started = BeginQuery();
        int candidates = 0;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (!candidate.IsActive || candidate is not T typed ||
                candidate.Collider is not { } shape)
            {
                continue;
            }
            if (bounds.Intersects(CollisionMath2D.GetBounds(shape, candidate.Transform)))
                results.Add(typed);
        }
        RecordQuery(QueryKind.Area, started, candidates, results.Count);
        return results.Count;
    }

    public IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius)
        where T : GameInstance
    {
        CollisionShape2D query = CollisionShape2D.Circle(radius);
        Transform2D transform = Transform2D.Default with { Position = center };
        long started = BeginQuery();
        int candidates = 0;
        List<T>? matches = null;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (!candidate.IsActive || candidate is not T typed ||
                candidate.Collider is not { } shape)
            {
                continue;
            }
            if (CollisionMath2D.Intersects(query, transform, shape, candidate.Transform))
                (matches ??= new List<T>()).Add(typed);
        }
        T[] result = matches?.ToArray() ?? Array.Empty<T>();
        RecordQuery(QueryKind.Radius, started, candidates, result.Length);
        return result;
    }

    public int QueryRadius<T>(
        Vector2D center,
        float radius,
        GameplayQueryBuffer<T> results) where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(results);
        CollisionShape2D query = CollisionShape2D.Circle(radius);
        Transform2D transform = Transform2D.Default with { Position = center };
        results.Clear();
        long started = BeginQuery();
        int candidates = 0;
        foreach (GameInstance candidate in _instances.Values)
        {
            candidates++;
            if (!candidate.IsActive || candidate is not T typed ||
                candidate.Collider is not { } shape)
            {
                continue;
            }
            if (CollisionMath2D.Intersects(query, transform, shape, candidate.Transform))
                results.Add(typed);
        }
        RecordQuery(QueryKind.Radius, started, candidates, results.Count);
        return results.Count;
    }

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
            Time.ReleaseOwner(instance.Id);
            instance.DetachDrawTracker(this);
            UnindexInstance(instance.Id);
            _instances.Remove(instance.Id);
            instance.DetachGameplayContext(_gameplay);
            RaiseEvent(new InstanceDestroyedEvent(instance.Id, instance.ObjectTypeName));
        }
        Time.ResetSceneState();
        ClearPendingMutations();
    }

    private bool CanReceiveInput(GameInstance instance) =>
        instance.IsActive && (!Time.IsPaused || instance.TimeMode == InstanceTimeMode.Unscaled);

    private static bool ShouldUpdate(GameInstance instance, GameplayTimeSnapshot time) =>
        instance.IsActive && (!time.IsPaused || instance.TimeMode == InstanceTimeMode.Unscaled);

    private static double DeltaFor(GameInstance instance, GameplayTimeSnapshot time) =>
        instance.TimeMode == InstanceTimeMode.Unscaled
            ? time.UnscaledDeltaTime
            : time.DeltaTime;

    private void CaptureInstances(List<GameInstance> destination)
    {
        destination.Clear();
        foreach (GameInstance instance in _instances.Values)
            destination.Add(instance);
    }

    private int CaptureDrawEntries(
        ISpriteBatch batch,
        string layerName,
        Bounds2D? viewBounds,
        out int culledInstances)
    {
        _drawSnapshot.Clear();
        culledInstances = 0;
        if (!_instancesByLayer.TryGetValue(layerName, out List<IndexedInstance>? instances))
            return 0;

        for (int i = 0; i < instances.Count; i++)
        {
            IndexedInstance indexed = instances[i];
            GameInstance instance = indexed.Instance;
            if (!instance.IsActive) continue;
            if (viewBounds is { } bounds && !IsVisibleInView(batch, instance, bounds))
            {
                culledInstances++;
                continue;
            }
            _drawSnapshot.Add(new DrawEntry(instance));
        }
        return instances.Count;
    }

    private static bool IsVisibleInView(
        ISpriteBatch batch,
        GameInstance instance,
        Bounds2D viewBounds)
    {
        if (instance.ViewCulling == InstanceViewCullingMode.AlwaysVisible)
            return true;

        Bounds2D localBounds;
        if (instance.LocalDrawBounds is { } explicitBounds)
        {
            localBounds = explicitBounds;
        }
        else
        {
            if (instance.Sprite.IsEmpty ||
                !batch.TryGetSpriteMetadata(instance.Sprite, out SpriteMetadata metadata))
            {
                return true;
            }
            localBounds = new Bounds2D(
                -metadata.Origin.X,
                -metadata.Origin.Y,
                metadata.Size.X - metadata.Origin.X,
                metadata.Size.Y - metadata.Origin.Y);
        }

        return viewBounds.Intersects(TransformDrawBounds(localBounds, instance.Transform));
    }

    private static Bounds2D TransformDrawBounds(Bounds2D local, Transform2D transform)
    {
        float cosine = MathF.Cos(transform.Rotation);
        float sine = MathF.Sin(transform.Rotation);
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        Include(local.Left, local.Top);
        Include(local.Right, local.Top);
        Include(local.Right, local.Bottom);
        Include(local.Left, local.Bottom);
        return new Bounds2D(minX, minY, maxX, maxY);

        void Include(float localX, float localY)
        {
            float scaledX = localX * transform.Scale.X;
            float scaledY = localY * transform.Scale.Y;
            float worldX =
                scaledX * cosine + scaledY * sine + transform.Position.X;
            float worldY =
                -scaledX * sine + scaledY * cosine + transform.Position.Y;
            minX = MathF.Min(minX, worldX);
            minY = MathF.Min(minY, worldY);
            maxX = MathF.Max(maxX, worldX);
            maxY = MathF.Max(maxY, worldY);
        }
    }

    void IInstanceDrawTracker.OnLayerChanged(
        GameInstance instance,
        string? previousLayer,
        string? currentLayer)
    {
        if (!_indexedInstances.TryGetValue(instance.Id, out IndexedInstance? indexed))
            return;

        RemoveFromLayer(indexed);
        indexed.LayerName = currentLayer;
        try
        {
            AddToLayer(indexed);
        }
        catch
        {
            indexed.LayerName = previousLayer;
            AddToLayer(indexed);
            throw;
        }
    }

    void IInstanceDrawTracker.OnDepthChanged(
        GameInstance instance,
        LayerDepth previousDepth,
        LayerDepth currentDepth)
    {
        if (!_indexedInstances.TryGetValue(instance.Id, out IndexedInstance? indexed))
            return;

        RemoveFromLayer(indexed);
        indexed.Depth = currentDepth;
        try
        {
            AddToLayer(indexed);
        }
        catch
        {
            indexed.Depth = previousDepth;
            AddToLayer(indexed);
            throw;
        }
    }

    private void IndexInstance(GameInstance instance)
    {
        var indexed = new IndexedInstance(
            instance,
            _nextDrawSequence++,
            instance.LayerName,
            instance.Depth);
        _indexedInstances.Add(instance.Id, indexed);
        AddToLayer(indexed);
    }

    private void UnindexInstance(InstanceId id)
    {
        if (!_indexedInstances.Remove(id, out IndexedInstance? indexed)) return;
        RemoveFromLayer(indexed);
        if (_indexedInstances.Count == 0)
        {
            _nextDrawSequence = 0;
            _instancesByLayer.Clear();
        }
    }

    private void AddToLayer(IndexedInstance indexed)
    {
        if (indexed.LayerName is null) return;
        if (!_instancesByLayer.TryGetValue(indexed.LayerName, out List<IndexedInstance>? instances))
        {
            instances = new List<IndexedInstance>();
            _instancesByLayer.Add(indexed.LayerName, instances);
        }
        int low = 0;
        int high = instances.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (CompareIndexedInstances(instances[middle], indexed) < 0)
                low = middle + 1;
            else
                high = middle;
        }
        instances.Insert(low, indexed);
    }

    private void RemoveFromLayer(IndexedInstance indexed)
    {
        if (indexed.LayerName is null ||
            !_instancesByLayer.TryGetValue(indexed.LayerName, out List<IndexedInstance>? instances))
        {
            return;
        }

        instances.Remove(indexed);
    }

    private static TimeSpan ToElapsedTime(long timestampTicks) =>
        timestampTicks == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(timestampTicks / (double)Stopwatch.Frequency);

    private long BeginQuery() =>
        _gameplayQueryStatisticsEnabled ? Stopwatch.GetTimestamp() : 0L;

    private void RecordQuery(
        QueryKind kind,
        long started,
        int candidates,
        int hits)
    {
        if (!_gameplayQueryStatisticsEnabled) return;
        ref QueryCounters counters = ref _queryCounters[(int)kind];
        counters.QueryCount++;
        counters.CandidateCount += candidates;
        counters.HitCount += hits;
        counters.ElapsedTimestampTicks += Stopwatch.GetTimestamp() - started;
    }

    private GameplayQueryMetric CaptureQueryMetric(QueryKind kind)
    {
        QueryCounters counters = _queryCounters[(int)kind];
        return new GameplayQueryMetric(
            counters.QueryCount,
            counters.CandidateCount,
            counters.HitCount,
            TimeSpan.FromSeconds(
                counters.ElapsedTimestampTicks / (double)Stopwatch.Frequency));
    }

    private void ResetGameplayQueryStatistics()
    {
        Array.Clear(_queryCounters);
        _querySampledSteps = 0;
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

    private readonly record struct DrawEntry(GameInstance Instance);

    private sealed class IndexedInstance(
        GameInstance instance,
        long sequence,
        string? layerName,
        LayerDepth depth)
    {
        public GameInstance Instance { get; } = instance;
        public long Sequence { get; } = sequence;
        public string? LayerName { get; set; } = layerName;
        public LayerDepth Depth { get; set; } = depth;
    }

    private enum QueryKind
    {
        Find,
        Collision,
        Area,
        Radius
    }

    private struct QueryCounters
    {
        public long QueryCount;
        public long CandidateCount;
        public long HitCount;
        public long ElapsedTimestampTicks;
    }

    private static int CompareIndexedInstances(IndexedInstance x, IndexedInstance y)
    {
        int depth = y.Depth.Value.CompareTo(x.Depth.Value);
        return depth != 0 ? depth : x.Sequence.CompareTo(y.Sequence);
    }

    private sealed class SceneGameplayContext(SceneAggregate owner) : IGameplayContext
    {
        public GameplayTimeController Time => owner.Time;

        public T Spawn<T>(T instance) where T : GameInstance => owner.QueueSpawn(instance);

        public T Spawn<T>(PrefabRef<T> prefab, Vector2D position) where T : GameInstance =>
            owner.QueueSpawn(owner._instanceFactory.Create(
                prefab,
                new PrefabSpawnContext(position)));

        public T Spawn<T, TArgs>(PrefabRef<T, TArgs> prefab, in TArgs args)
            where T : GameInstance =>
            owner.QueueSpawn(owner._instanceFactory.Create(prefab, args));

        public void Destroy(InstanceId id) => owner.QueueDestroy(id);

        public GameInstance? FindById(InstanceId id) => owner.FindById(id);

        public T? FindFirst<T>() where T : GameInstance => owner.FindFirst<T>();

        public IReadOnlyList<T> FindAll<T>() where T : GameInstance => owner.FindAll<T>();

        public int FindAll<T>(GameplayQueryBuffer<T> results) where T : GameInstance =>
            owner.FindAll(results);

        public int CountInstances<T>() where T : GameInstance => owner.CountInstances<T>();

        public T? FirstCollision<T>(GameInstance source) where T : GameInstance =>
            owner.FirstCollision<T>(source);

        public IReadOnlyList<T> Collisions<T>(GameInstance source) where T : GameInstance =>
            owner.Collisions<T>(source);

        public int Collisions<T>(GameInstance source, GameplayQueryBuffer<T> results)
            where T : GameInstance => owner.Collisions(source, results);

        public IReadOnlyList<T> QueryArea<T>(Bounds2D bounds) where T : GameInstance =>
            owner.QueryArea<T>(bounds);

        public int QueryArea<T>(Bounds2D bounds, GameplayQueryBuffer<T> results)
            where T : GameInstance => owner.QueryArea(bounds, results);

        public IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius)
            where T : GameInstance => owner.QueryRadius<T>(center, radius);

        public int QueryRadius<T>(
            Vector2D center,
            float radius,
            GameplayQueryBuffer<T> results) where T : GameInstance =>
            owner.QueryRadius(center, radius, results);

        public void RequestScene(SceneRef scene)
        {
            if (scene.IsEmpty)
                throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
            (owner._sceneSwitchRequester ?? throw new InvalidOperationException(
                "Scene switching is not configured for this Scene.")).Request(scene);
        }

        public void RequestScene<TArgs>(SceneRef<TArgs> scene, in TArgs args)
            where TArgs : struct
        {
            if (scene.IsEmpty)
                throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
            (owner._sceneSwitchRequester ?? throw new InvalidOperationException(
                "Scene switching is not configured for this Scene.")).Request(scene, args);
        }

        public void PauseGameplay(GameInstance instance, GameplayPauseKey key)
        {
            RequireOwner(instance);
            owner.Time.Pause(instance.Id, key);
        }

        public void ResumeGameplay(GameInstance instance, GameplayPauseKey key)
        {
            RequireOwner(instance);
            owner.Time.Resume(instance.Id, key);
        }

        public void ToggleGameplayPause(GameInstance instance, GameplayPauseKey key)
        {
            RequireOwner(instance);
            owner.Time.Toggle(instance.Id, key);
        }

        public void ReleaseGameplayPauses(GameInstance instance)
        {
            RequireOwner(instance);
            owner.Time.ReleaseOwner(instance.Id);
        }

        private static void RequireOwner(GameInstance instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
        }
    }

    private sealed class UntypedSceneSwitchRequester(Action<SceneRef> request)
        : ISceneSwitchRequester
    {
        public void Request(SceneRef scene) => request(scene);

        public void Request<TArgs>(SceneRef<TArgs> scene, in TArgs args)
            where TArgs : struct => throw new InvalidOperationException(
                "This Scene switch requester only supports untyped SceneRef values.");
    }
}
