namespace GameEngine.Features.Camera.Application;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 拉相机到指定位置（带可选震屏）。
/// Camera 切片专属命令，由 CameraCommandHandler 处理。
/// </summary>
public sealed record FocusCameraCommand(
    SceneAggregate Scene,
    Vector2D TargetPosition,
    float Zoom = 1.0f,
    float ShakeDuration = 0f,
    float ShakeMagnitude = 0f
);
