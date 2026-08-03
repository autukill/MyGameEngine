namespace GameEngine.Core.Domain.Entities;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;

public class GameInstance
{
    public InstanceId Id { get; }
    public string ObjectTypeName { get; }
    public Transform2D Transform { get; private set; }
    public LayerDepth Depth { get; private set; }
    public bool IsActive { get; private set; } = true;

    public GameInstance(InstanceId id, string objectTypeName, Transform2D transform, LayerDepth depth)
    {
        Id = id;
        ObjectTypeName = objectTypeName;
        Transform = transform;
        Depth = depth;
    }

    /// <summary>
    /// 战术行为：移动实例，触发移动领域事件
    /// </summary>
    public void MoveTo(Vector2D newPosition, Action<IDomainEvent> raiseEvent)
    {
        if (Transform.Position == newPosition) return;

        var oldPos = Transform.Position;
        Transform = Transform with { Position = newPosition };

        // 发送领域事件，物理系统捕获后自动更新 QuadTree
        raiseEvent(new InstanceMovedEvent(Id, oldPos, newPosition));
    }

    /// <summary>
    /// 战术行为：提交 Stencil 遮罩绘制指令（解决 GMS 底层黑盒痛点）
    /// </summary>
    public void RequestStencilMask(Action drawMaskShape, Action drawContent, Action<IDomainEvent> raiseEvent)
    {
        raiseEvent(new StencilMaskPassRequestedEvent(Id, drawMaskShape, drawContent));
    }

    public void Destroy()
    {
        IsActive = false;
    }
}
