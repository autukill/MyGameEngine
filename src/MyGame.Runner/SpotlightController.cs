namespace MyGame.Runner;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Infrastructure;

/// <summary>
/// 聚光灯业务实例：在 Step 中轮询鼠标位置，在 Key Down 中处理退出。
/// Program 只注入 Pass 与关闭窗口回调，不再直接管理输入设备。
/// </summary>
public sealed class SpotlightController : GameInstance
{
    private readonly StencilMaskPass _stencilPass;
    private readonly Vector2D _initialCenter;
    private readonly float _radius;
    private readonly Action _closeWindow;

    public SpotlightController(
        StencilMaskPass stencilPass,
        Vector2D initialCenter,
        float radius,
        Action closeWindow)
    {
        _stencilPass = stencilPass;
        _initialCenter = initialCenter;
        _radius = radius;
        _closeWindow = closeWindow;
    }

    public override void OnCreate() =>
        _stencilPass.SetMaskCircle(new Vector2(_initialCenter.X, _initialCenter.Y), _radius);

    public override void OnStep(double deltaTime)
    {
        if (Input is null) return;
        var mouse = Input.MousePosition;
        _stencilPass.SetMaskCircle(new Vector2(mouse.X, mouse.Y), _radius);
    }

    public override void OnKeyDown(InputKey key)
    {
        if (key == InputKey.Escape)
            _closeWindow();
    }
}
