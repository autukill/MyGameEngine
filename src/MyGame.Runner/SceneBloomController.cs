namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Application;
using GameEngine.Features.ToneMapping.Domain;

public sealed class SceneBloomController(
    Action<IDomainEvent> raiseEvent,
    BloomSettings bloomSettings,
    ToneMappingSettings toneMappingSettings) : GameInstance {
    private readonly Action<IDomainEvent> _raiseEvent = raiseEvent ?? throw new ArgumentNullException( nameof( raiseEvent ) );

    public override void OnCreate()
    {
        this.RequestBloom(
            bloomSettings,
            _raiseEvent,
            colorFormat: RenderTargetColorFormat.Rgba16Float,
            encoding: RenderSurfaceEncoding.Linear,
            presentation: BloomPresentation.SurfaceOnly);
        this.RequestToneMapping(
            toneMappingSettings,
            _raiseEvent,
            bloomSource: BloomEffectDescriptor.GlowOutput(BloomEffectDescriptor.DefaultKey));
    }

    public override void OnDestroy()
    {
        this.ReleaseToneMapping(_raiseEvent);
        this.ReleaseBloom(_raiseEvent);
    }
}
