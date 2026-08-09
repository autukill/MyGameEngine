namespace GameEngine.Core.Domain.Entities;

using System.Numerics;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
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
    public InstanceId Id { get; } = InstanceId.New();

    /// <summary>对象类型名（默认取运行时类名，对应 GMS 的 object_name）</summary>
    public string ObjectTypeName { get; protected set; }

    /// <summary>当前变换状态（位置/旋转/缩放）</summary>
    public Transform2D Transform { get; protected set; } = Transform2D.Default;

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
