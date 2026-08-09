namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Presentation.Application;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>声明 LDR GUI Surface 始终位于色调映射结果之上。</summary>
public sealed class SceneGuiPresentationController(Action<IDomainEvent> raiseEvent) : GameInstance
{
    public override void OnCreate() => this.RequestPresentSurface(
        RenderSurfaceKey.SceneGui,
        raiseEvent,
        layer: 1000,
        blend: PresentationBlendMode.AlphaBlend);

    public override void OnDestroy() => this.ReleasePresentSurface(raiseEvent);
}
