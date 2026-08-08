namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>只声明 Spotlight 意图；不持有 Pass、RenderTarget 或其他 GPU 对象。</summary>
public sealed class SpotlightController : GameInstance
{
    private readonly Action<IDomainEvent> _raiseEvent;
    private readonly Vector2D _initialCenter;
    private readonly float _radius;
    private readonly Action _closeWindow;

    public SpotlightController(
        Action<IDomainEvent> raiseEvent,
        Vector2D initialCenter,
        float radius,
        Action closeWindow)
    {
        _raiseEvent = raiseEvent;
        _initialCenter = initialCenter;
        _radius = radius;
        _closeWindow = closeWindow;
    }

    public override void OnCreate() => Request(_initialCenter);

    public override void OnStep(double deltaTime)
    {
        if (Input is null) return;
        Request(Input.MousePosition);
    }

    public override void OnDestroy() => this.ReleaseStencilMask(_raiseEvent);

    public override void OnKeyDown(InputKey key)
    {
        if (key == InputKey.Escape) _closeWindow();
    }

    private void Request(Vector2D center) =>
        this.RequestStencilMask(
            center,
            _radius,
            StencilMaskState.Spotlight,
            _raiseEvent);
}
