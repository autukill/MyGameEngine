namespace GameEngine.Core.Domain.Entities;

using System.Numerics;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
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

    /// <summary>是否激活（停用后不参与 Step/Draw）</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>持久标记（场景切换时是否保留，对应 GMS persistent=true）</summary>
    public bool IsPersistent { get; set; }

    /// <summary>精灵引用（对应 GMS 的 sprite_index）</summary>
    public SpriteRef Sprite { get; set; } = SpriteRef.Empty;

    /// <summary>自定义属性包（给 AI Agent / 脚本动态写入临时状态用）</summary>
    public Dictionary<string, object> Properties { get; } = new();

    /// <summary>可选：实例的颜色着色（GMS 的 image_blend）</summary>
    public Vector4 Color { get; set; } = Vector4.One;

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

    /// <summary>Step 事件：每个逻辑帧调用——主游戏逻辑写这里</summary>
    public virtual void OnStep(double deltaTime) { }

    /// <summary>
    /// Draw 事件：每个渲染帧调用。
    /// 默认实现：在 Transform.Position 处画 Sprite，使用 Color 着色。
    /// 子类可 override 画自定义几何。
    /// </summary>
    public virtual void OnDraw(ISpriteBatch batch)
    {
        if (Sprite.IsEmpty) return;
        batch.Draw(
            textureHandle: Sprite.TextureHandle,
            position: new Vector2(Transform.Position.X, Transform.Position.Y),
            size: new Vector2(Sprite.Width, Sprite.Height),
            color: Color,
            uvBounds: Sprite.UvBounds);
    }

    /// <summary>Destroy 事件：实例被销毁时调用</summary>
    public virtual void OnDestroy() { }

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
