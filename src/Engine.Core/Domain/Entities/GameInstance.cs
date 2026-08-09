namespace GameEngine.Core.Domain.Entities;

using System.Numerics;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 游戏实例实体（对应 GMS 的 Object Instance）。
///
/// GMS 风格事件模型：
///   - OnCreate():  实例被加入场景时调用一次（GMS: Create event）
///   - OnStep(dt):  每个逻辑帧调用（GMS: Step event）—— 主要游戏逻辑写这里
///   - OnDraw(batch): 每个渲染帧调用（GMS: Draw event）—— 默认实现画 Sprite
///   - OnDestroy(): 实例被销毁时调用一次（GMS: Destroy event）
///
/// 子类通过 override 实现具体行为，就像 GMS 中给 Object 添加事件代码一样。
///
/// DDD 战术特征：
///   - 强类型 InstanceId 标识
///   - 状态变更通过 MoveTo/SetActive 等方法触发领域事件
///   - 不包含任何切片专属方法（切片通过扩展方法挂行为，见 GameInstanceStencilExtensions）
/// </summary>
public class GameInstance
{
    private IGameplayContext? _gameplay;
    private Dictionary<AlarmId, double>? _alarms;
    private List<AlarmId>? _alarmKeys;
    private List<AlarmId>? _firedAlarms;

    public InstanceId Id { get; } = InstanceId.New();

    /// <summary>对象类型名（默认取运行时类名，对应 GMS 的 object_name）</summary>
    public string ObjectTypeName { get; protected set; }

    /// <summary>当前变换状态（位置/旋转/缩放）</summary>
    public Transform2D Transform { get; protected set; } = Transform2D.Default;

    /// <summary>High-frequency gameplay position convenience over Transform.</summary>
    public Vector2D Position
    {
        get => Transform.Position;
        set => Transform = Transform with { Position = value };
    }

    /// <summary>Rotation in the engine's existing counter-clockwise radians convention.</summary>
    public float Rotation
    {
        get => Transform.Rotation;
        set => Transform = Transform with { Rotation = value };
    }

    public Vector2D Scale
    {
        get => Transform.Scale;
        set => Transform = Transform with { Scale = value };
    }

    /// <summary>图层深度（决定渲染顺序，对应 GMS depth）</summary>
    public LayerDepth Depth { get; protected set; } = LayerDepth.Instances;

    /// <summary>
    /// 归属的图层名称（对应 GMS 中间接的 Layer 概念）。
    /// 默认 null——加入 SceneAggregate 时由聚合根补填为 "Instances"。
    /// SceneAggregate.DrawActive 按此字段分组渲染。
    /// </summary>
    public string? LayerName { get; set; }

    /// <summary>是否激活（停用后不参与 Step/Draw）</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>持久标记（场景切换时是否保留，对应 GMS persistent=true）</summary>
    public bool IsPersistent { get; set; }

    /// <summary>精灵引用（对应 GMS 的 sprite_index）</summary>
    public SpriteRef Sprite { get; set; } = SpriteRef.Empty;

    /// <summary>Optional lightweight collider used by Scene gameplay queries.</summary>
    public CollisionShape2D? Collider { get; set; }

    /// <summary>当前动画帧（对应 GMS image_index，可为小数）。</summary>
    public float ImageIndex { get; set; }

    /// <summary>Sprite 基础 FPS 的播放倍率；0=暂停，负数=反向。</summary>
    public float ImageSpeed { get; set; } = 1f;

    /// <summary>自定义属性包（给 AI Agent / 脚本动态写入临时状态用）</summary>
    public Dictionary<string, object> Properties { get; } = new();

    /// <summary>可选：实例的颜色着色（GMS 的 image_blend）</summary>
    public Vector4 Color { get; set; } = Vector4.One;

    /// <summary>
    /// 实例级渲染状态（GMS gpu_set_blendmode + depth 的升级版）。
    /// 由 SceneAggregate.DrawActive 在 OnDraw 前应用，变更自动 Flush。
    /// </summary>
    public RenderStyle RenderStyle { get; set; } = RenderStyle.Default;

    /// <summary>
    /// 实例使用的 Shader（GMS shader_index）。仅持名字，由渲染层 ShaderLibrary 解析。
    /// null = 使用 Pass 默认 shader。
    /// </summary>
    public ShaderRef? Shader { get; set; }

    /// <summary>
    /// Optional material instance. When set it takes precedence over Shader and supplies typed
    /// uniform values while preserving a logical reference across shader hot replacement.
    /// </summary>
    public MaterialRef? Material { get; set; }

    /// <summary>
    /// 输入提供者（GMS keyboard_check / mouse_x 等价物）。
    /// 由 SceneAggregate.Add/SetInput 自动注入；OnStep 中轮询查询。
    /// </summary>
    public IInputProvider? Input { get; set; }

    /// <summary>Non-null input access for ordinary gameplay code.</summary>
    protected IInputProvider Controls => Input ?? NullInputProvider.Instance;

    /// <summary>True after the instance has been added or queued for a Scene.</summary>
    protected bool HasGameplayContext => _gameplay is not null;

    /// <summary>Sprite 元数据/帧解析器，由 SceneAggregate 注入。</summary>
    public ISpriteResolver? SpriteResolver { get; set; }

    protected GameInstance()
    {
        ObjectTypeName = GetType().Name;
    }

    /// <summary>兼容旧版 Spawn(string, ...) 的构造函数</summary>
    public GameInstance(string objectTypeName, Vector2D position, LayerDepth depth)
    {
        ObjectTypeName = objectTypeName;
        Transform = Transform2D.Default with { Position = position };
        Depth = depth;
    }

    // ============ GMS 风格事件钩子（子类 override） ============

    /// <summary>Create 事件：实例被加入场景时调用一次</summary>
    public virtual void OnCreate() { }

    /// <summary>Begin Step 事件：所有实例先执行——输入预处理/状态缓存（GMS Begin Step）</summary>
    public virtual void OnBeginStep(double deltaTime) { }

    /// <summary>Step 事件：每个逻辑帧调用——主游戏逻辑写这里</summary>
    public virtual void OnStep(double deltaTime) { }

    /// <summary>End Step 事件：所有实例后执行——校验/后处理（GMS End Step）</summary>
    public virtual void OnEndStep(double deltaTime) { }

    /// <summary>
    /// Draw 事件：每个渲染帧调用。
    /// 默认实现：在 Transform.Position 处画 Sprite，使用 Color 着色。
    /// 子类可 override 画自定义几何。
    /// </summary>
    /// <summary>
    /// Draw Begin 事件：OnDraw 之前调用（GMS Draw Begin）。
    /// 用于设置该实例的 shader/blend 等渲染状态（命令式次路径，主路径用 RenderStyle）。
    /// </summary>
    public virtual void OnBeginDraw(ISpriteBatch batch) { }

    public void DrawSelf(ISpriteBatch batch)
    {
        if (Sprite.IsEmpty) return;
        batch.DrawSpriteExt(
            Sprite,
            ImageIndex,
            new Vector2(Transform.Position.X, Transform.Position.Y),
            new Vector2(Transform.Scale.X, Transform.Scale.Y),
            Transform.Rotation,
            Color);
    }

    public virtual void OnDraw(ISpriteBatch batch) => DrawSelf(batch);

    /// <summary>
    /// Draw End 事件：OnDraw 之后调用（GMS Draw End）。
    /// 若在 OnBeginDraw/OnDraw 中手动改了状态，应在此复位；主路径由 Pass.End() 兜底复位。
    /// </summary>
    public virtual void OnEndDraw(ISpriteBatch batch) { }

    /// <summary>
    /// Draw GUI 事件：屏幕空间 UI 绘制，不受相机影响（GMS Draw GUI）。
    /// 在 EngineWindow.DrawGUI 阶段由 SceneAggregate.DrawGUI 调度。
    /// </summary>
    public virtual void OnDrawGUI(ISpriteBatch batch) { }

    /// <summary>Key Down 事件（GMS Key Down 事件）：由场景 PerformInput 分发</summary>
    public virtual void OnKeyDown(InputKey key) { }

    /// <summary>Key Up 事件（GMS Key Up 事件）：由场景 PerformInput 分发</summary>
    public virtual void OnKeyUp(InputKey key) { }

    /// <summary>Destroy 事件：实例被销毁时调用</summary>
    public virtual void OnDestroy() { }

    /// <summary>Called before Begin Step when a scheduled alarm reaches zero.</summary>
    public virtual void OnAlarm(AlarmId alarm) { }

    public void MoveBy(Vector2D delta) => Position += delta;

    public void RotateBy(float deltaRadians) => Rotation += deltaRadians;

    public void ScaleBy(Vector2D factor) => Scale = new Vector2D(
        Scale.X * factor.X,
        Scale.Y * factor.Y);

    protected bool KeyDown(InputKey key) => Controls.IsKeyDown(key);

    protected bool KeyPressed(InputKey key) => Controls.WasKeyPressed(key);

    protected bool KeyReleased(InputKey key) => Controls.WasKeyReleased(key);

    protected Vector2D InputAxis2D(
        InputKey left = InputKey.A,
        InputKey right = InputKey.D,
        InputKey up = InputKey.W,
        InputKey down = InputKey.S) => Controls.Axis2D(left, right, up, down);

    protected T Spawn<T>(T instance) where T : GameInstance =>
        RequireGameplay().Spawn(instance);

    protected T Spawn<T>(PrefabRef<T> prefab, Vector2D position) where T : GameInstance =>
        RequireGameplay().Spawn(prefab, position);

    protected T Spawn<T, TArgs>(PrefabRef<T, TArgs> prefab, in TArgs args)
        where T : GameInstance => RequireGameplay().Spawn(prefab, args);

    protected void DestroySelf() => RequireGameplay().Destroy(Id);

    protected void Destroy(GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        RequireGameplay().Destroy(instance.Id);
    }

    protected GameInstance? FindById(InstanceId id) => RequireGameplay().FindById(id);

    protected T? FindFirst<T>() where T : GameInstance => RequireGameplay().FindFirst<T>();

    protected IReadOnlyList<T> FindAll<T>() where T : GameInstance =>
        RequireGameplay().FindAll<T>();

    /// <summary>Returns the first active T whose collider overlaps this instance.</summary>
    protected T? FirstCollision<T>() where T : GameInstance =>
        RequireGameplay().FirstCollision<T>(this);

    /// <summary>Returns all active T instances whose colliders overlap this instance.</summary>
    protected IReadOnlyList<T> Collisions<T>() where T : GameInstance =>
        RequireGameplay().Collisions<T>(this);

    protected IReadOnlyList<T> QueryArea<T>(Bounds2D bounds) where T : GameInstance =>
        RequireGameplay().QueryArea<T>(bounds);

    protected IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius)
        where T : GameInstance => RequireGameplay().QueryRadius<T>(center, radius);

    /// <summary>Requests a registered Scene switch at the safe boundary after the current Step.</summary>
    protected void SwitchScene(SceneRef scene) => RequireGameplay().RequestScene(scene);

    public void SetAlarm(AlarmId alarm, double seconds)
    {
        if (alarm.IsEmpty)
            throw new ArgumentException("Alarm cannot be empty.", nameof(alarm));
        if (!double.IsFinite(seconds) || seconds < 0d)
            throw new ArgumentOutOfRangeException(
                nameof(seconds), "Alarm delay must be finite and non-negative.");
        (_alarms ??= new Dictionary<AlarmId, double>())[alarm] = seconds;
    }

    public bool CancelAlarm(AlarmId alarm) => _alarms?.Remove(alarm) == true;

    public bool IsAlarmSet(AlarmId alarm) => _alarms?.ContainsKey(alarm) == true;

    internal void AdvanceSpriteAnimation(double deltaTime)
    {
        if (Sprite.IsEmpty || SpriteResolver is null || ImageSpeed == 0f) return;
        if (!SpriteResolver.TryGetMetadata(Sprite, out var metadata)) return;
        if (metadata.FrameCount <= 1 || metadata.FramesPerSecond <= 0f) return;

        float frameCount = metadata.FrameCount;
        float next = ImageIndex + metadata.FramesPerSecond * ImageSpeed * (float)deltaTime;
        next %= frameCount;
        if (next < 0f) next += frameCount;
        ImageIndex = next;
    }

    internal void AdvanceAlarms(double deltaTime)
    {
        if (_alarms is not { Count: > 0 }) return;

        _alarmKeys ??= new List<AlarmId>(_alarms.Count);
        _firedAlarms ??= new List<AlarmId>();
        _alarmKeys.Clear();
        _firedAlarms.Clear();
        _alarmKeys.AddRange(_alarms.Keys);

        for (int i = 0; i < _alarmKeys.Count; i++)
        {
            AlarmId alarm = _alarmKeys[i];
            if (!_alarms.TryGetValue(alarm, out double remaining)) continue;
            remaining -= deltaTime;
            if (remaining <= 0d)
            {
                _alarms.Remove(alarm);
                _firedAlarms.Add(alarm);
            }
            else
            {
                _alarms[alarm] = remaining;
            }
        }

        for (int i = 0; i < _firedAlarms.Count; i++)
            OnAlarm(_firedAlarms[i]);
    }

    internal void AttachGameplayContext(IGameplayContext gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        if (_gameplay is not null && !ReferenceEquals(_gameplay, gameplay))
            throw new InvalidOperationException("Instance already belongs to another Scene.");
        _gameplay = gameplay;
    }

    internal void DetachGameplayContext(IGameplayContext gameplay)
    {
        if (!ReferenceEquals(_gameplay, gameplay)) return;
        _gameplay = null;
        _alarms?.Clear();
    }

    private IGameplayContext RequireGameplay() => _gameplay ??
        throw new InvalidOperationException(
            "This gameplay operation requires the instance to belong to a Scene.");

    // ============ DDD 战术行为（状态变更 → 领域事件） ============

    /// <summary>移动实例到新位置，触发 InstanceMovedEvent</summary>
    public void MoveTo(Vector2D newPosition, Action<IDomainEvent> raiseEvent)
    {
        if (!IsActive) return;

        var oldPosition = Transform.Position;
        if (oldPosition == newPosition) return;

        Transform = Transform with { Position = newPosition };
        raiseEvent(new InstanceMovedEvent(Id, oldPosition, newPosition));
    }

    /// <summary>切换激活状态，触发 InstanceActivationChangedEvent</summary>
    public void SetActive(bool active, Action<IDomainEvent> raiseEvent)
    {
        if (IsActive == active) return;
        IsActive = active;
        raiseEvent(new InstanceActivationChangedEvent(Id, active));
    }

    /// <summary>修改图层深度</summary>
    public void ChangeDepth(LayerDepth newDepth, Action<IDomainEvent> raiseEvent)
    {
        if (Depth == newDepth) return;
        Depth = newDepth;
    }

    public override string ToString() =>
        $"{ObjectTypeName}#{Id} @ {Transform.Position} depth={Depth.Value} active={IsActive}";
}
