namespace GameEngine.Features.Camera.Application;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Camera 切片的命令处理器。
/// 把 FocusCameraCommand 翻译为相机位置/缩放/震屏的实际变更。
///
/// 注意：本 Handler 目前只打印日志。真正的相机移动由
/// SceneRenderContext (Infrastructure) 订阅本命令后实现。
/// 在 Phase 1.3 Demo 中，鼠标位置直接驱动 RenderContext.MainCamera.Position，
/// 此处仅作命令通道演示。Phase 1.4 之后会接入事件总线。
/// </summary>
public static class CameraCommandHandler
{
    public static void Handle(FocusCameraCommand cmd)
    {
        Console.WriteLine(
            $"[CameraHandler] Camera focus @ {cmd.TargetPosition} zoom={cmd.Zoom} " +
            $"shake={cmd.ShakeMagnitude} for {cmd.ShakeDuration}s");
    }
}
