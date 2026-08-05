namespace GameEngine.Features.StencilMasking.Application;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// GameInstance 的 StencilMask 切片扩展方法。
///
/// 为什么用扩展方法而不是放在 GameInstance 内：
///   GameInstance 属于共享内核 (Engine.Core/Domain)，不能反向依赖任何切片。
///   把 RequestStencilMask 放在 GameInstance 内会让共享内核依赖
///   StencilMaskPassRequestedEvent（切片专属类型），违反 VSA 依赖方向。
///   用扩展方法把方法"挂"到 GameInstance 上，依赖方向：
///     切片 ──依赖──> 共享内核 (GameInstance + IDomainEvent)
///   而不是：
///     共享内核 ──依赖──> 切片 (StencilMaskPassRequestedEvent)  ❌
/// </summary>
public static class GameInstanceStencilExtensions
{
    /// <summary>
    /// 请求一次 Stencil 遮罩 Pass。
    /// 会触发 StencilMaskPassRequestedEvent，由渲染切片订阅后构造 StencilMaskPass。
    /// </summary>
    public static void RequestStencilMask(
        this GameInstance instance,
        Action renderMaskShape,
        Action renderMaskedContent,
        Action<IDomainEvent> raiseEvent)
    {
        if (!instance.IsActive) return;

        raiseEvent(new StencilMaskPassRequestedEvent(
            ProviderId: instance.Id,
            RenderMaskShape: renderMaskShape,
            RenderMaskedContent: renderMaskedContent));
    }
}
