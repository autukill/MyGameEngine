namespace GameEngine.Features.StencilMasking.Application;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>
/// StencilMask 切片的命令处理器。
/// 把 Command 翻译为对 GameInstance.RequestStencilMask()（扩展方法）的调用，
/// 让聚合根发出 StencilMaskPassRequestedEvent。
/// 渲染切片会订阅此事件并构造对应的 StencilMaskPass。
/// </summary>
public static class StencilMaskCommandHandler
{
    public static void Handle(ApplySpotlightMaskCommand cmd)
    {
        var instance = cmd.Scene.FindById(cmd.SpotlightId);
        if (instance is null)
        {
            Console.WriteLine($"[StencilHandler] WARN: spotlight instance {cmd.SpotlightId} not found");
            return;
        }

        var maskCenter = cmd.MaskCenter;
        var maskRadius = cmd.MaskRadius;
        var maskState = cmd.MaskState;

        instance.RequestStencilMask(
            renderMaskShape: () =>
            {
                // 这里仅声明意图，真正的几何绘制由 Infrastructure 层
                // (StencilMaskPass) 捕获事件后实现。Application 层不感知 OpenGL。
                Console.WriteLine(
                    $"[Spotlight] Mask shape at {maskCenter}, r={maskRadius}, mode={maskState.Mode}");
            },
            renderMaskedContent: () =>
            {
                Console.WriteLine("[Spotlight] Masked content drawing");
            },
            raiseEvent: ev => cmd.Scene.RaiseEvent(ev));
    }
}
