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
    RenderViewRef view,
    RenderSurfaceKey sceneColor,
    RenderViewEffects effects) : GameInstance
{
    private readonly RenderEffectKey _bloomKey = new(
        BloomEffectDescriptor.EffectKind,
        view.Name);
    private readonly RenderEffectKey _toneMappingKey = new(
        ToneMappingEffectDescriptor.EffectKind,
        view.Name);

    public override void OnCreate()
    {
        if (!effects.IsHdr) return;

        RenderSurfaceKey? bloomSource = null;
        if (effects.Bloom is { } bloom)
        {
            this.RequestBloom(
                bloom,
                raiseEvent,
                key: _bloomKey,
                source: sceneColor,
                colorFormat: RenderTargetColorFormat.Rgba16Float,
                encoding: RenderSurfaceEncoding.Linear);
            bloomSource = BloomEffectDescriptor.GlowOutput(_bloomKey);
        }

        this.RequestToneMapping(
            effects.ToneMapping!.Value,
            raiseEvent,
            key: _toneMappingKey,
            source: sceneColor,
            bloomSource: bloomSource);
    }

    public override void OnDestroy()
    {
        if (!effects.IsHdr) return;
        this.ReleaseToneMapping(raiseEvent, _toneMappingKey);
        if (effects.Bloom is not null) this.ReleaseBloom(raiseEvent, _bloomKey);
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
