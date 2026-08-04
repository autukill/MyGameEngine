namespace GameEngine.Core.Events;

[AttributeUsage(AttributeTargets.Method)]
public class OnStepAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class OnDrawAttribute : Attribute { }

// 逻辑层触发的碰撞事件领域消息
public readonly record struct CollisionOccurredEvent(
    uint SourceId, 
    uint TargetId, 
    System.Numerics.Vector2 ContactPoint
);
