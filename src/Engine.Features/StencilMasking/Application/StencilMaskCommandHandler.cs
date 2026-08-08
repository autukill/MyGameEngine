namespace GameEngine.Features.StencilMasking.Application;

/// <summary>把 Spotlight 命令翻译为类型化的持久渲染效果请求。</summary>
public static class StencilMaskCommandHandler
{
    public static void Handle(ApplySpotlightMaskCommand command)
    {
        var instance = command.Scene.FindById(command.SpotlightId);
        if (instance is null)
        {
            Console.WriteLine($"[StencilHandler] WARN: spotlight instance {command.SpotlightId} not found");
            return;
        }

        instance.RequestStencilMask(
            command.MaskCenter,
            command.MaskRadius,
            command.MaskState,
            command.Scene.RaiseEvent);
    }
}
