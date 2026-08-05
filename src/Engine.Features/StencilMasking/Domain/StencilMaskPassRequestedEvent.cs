namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 请求模板遮罩渲染 Pass 事件。
/// 关键突破：解耦 GameMaker 无法操作 GL_STENCIL_TEST 的痛点。
///
/// 设计哲学：
///   - 领域层（GameInstance）只声明"我要做遮罩"的意图（ProviderId + 两个绘制回调）
///   - "如何做"（ShowInside / ShowOutside / StencilRef / MaskBits）由 StencilMaskState 值对象描述，
///     通常通过 Command 在 Pass 配置阶段设定，不在事件里耦合
///   - 渲染层（StencilMaskCommandHandler）捕获事件后构造 StencilMaskPass 并加入 RenderPipeline
///
/// 为什么事件里仍保留 Action 回调：
///   Action 是 .NET 基础类型，不引用 OpenGL，不破坏领域纯净性。
///   让请求方在自己的上下文里声明"画什么形状的遮罩、画什么被遮罩的内容"，
///   比把几何参数硬编码到事件里更灵活（圆/矩形/任意多边形/动画路径都能复用同一事件类型）。
/// </summary>
public sealed record StencilMaskPassRequestedEvent(
    /// <summary>请求方的实例 ID（用于追踪与去重）</summary>
    InstanceId ProviderId,

    /// <summary>遮罩几何绘制回调（由 Requester 提供的绘制函数）</summary>
    Action RenderMaskShape,

    /// <summary>遮罩内容绘制回调</summary>
    Action RenderMaskedContent
) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

