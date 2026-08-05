namespace GameEngine.Core.Domain.Events;

using System;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 领域事件标记接口。
/// 所有领域事件必须实现此接口，并通过聚合根的 RaiseEvent 方法发出。
///
/// 设计原则：
///   - 事件是"已发生事实"的不可变记录，只描述 What happened，不携带 How to do
///   - 不包含聚合根 ID（聚合根上下文由 Command/Handler 层在调用时持有）
///   - OccurredOn 用 DateTime 而非 long Ticks，可读性优先（性能敏感场景可后续优化）
/// </summary>
public interface IDomainEvent
{
    /// <summary>事件发生时间（UTC）</summary>
    DateTime OccurredOn { get; }
}
