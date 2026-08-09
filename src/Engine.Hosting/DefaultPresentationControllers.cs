namespace GameEngine.Hosting;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Presentation.Application;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Application;
using GameEngine.Features.ToneMapping.Domain;

internal sealed class DefaultWorldEffectsController(
    Action<IDomainEvent> raiseEvent,
    Default2DRendererPlan renderer) : GameInstance
{
    public override void OnCreate()
    {
        if (!renderer.HdrEnabled) return;

        RenderSurfaceKey? bloomSource = null;
        if (renderer.Bloom is { } bloom)
        {
            this.RequestBloom(
                bloom,
                raiseEvent,
                colorFormat: RenderTargetColorFormat.Rgba16Float,
                encoding: RenderSurfaceEncoding.Linear);
            bloomSource = BloomEffectDescriptor.GlowOutput(
                BloomEffectDescriptor.DefaultKey);
        }

        this.RequestToneMapping(
            renderer.ToneMapping,
            raiseEvent,
            bloomSource: bloomSource);
    }

    public override void OnDestroy()
    {
        if (!renderer.HdrEnabled) return;
        this.ReleaseToneMapping(raiseEvent);
        if (renderer.Bloom is not null) this.ReleaseBloom(raiseEvent);
    }
}

internal sealed class DefaultWorldPresentationController(
    Action<IDomainEvent> raiseEvent,
    RenderSurfaceKey source,
    SingleCameraViewportDefinition viewport,
    int layer,
    PresentationBlendMode blend) : GameInstance
{
    public override void OnCreate() => this.RequestPresentSurface(
        source,
        raiseEvent,
        layer: layer,
        blend: blend,
        viewport: viewport.Viewport,
        fit: viewport.Fit);

    public override void OnDestroy() => this.ReleasePresentSurface(raiseEvent);
}

internal sealed class DefaultGuiPresentationController(
    Action<IDomainEvent> raiseEvent) : GameInstance
{
    public override void OnCreate() => this.RequestPresentSurface(
        RenderSurfaceKey.SceneGui,
        raiseEvent,
        layer: 1000,
        blend: PresentationBlendMode.AlphaBlend);

    public override void OnDestroy() => this.ReleasePresentSurface(raiseEvent);
}
