namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;

public sealed class SceneBloomController( Action<IDomainEvent> raiseEvent, BloomSettings settings ) : GameInstance {
    private readonly Action<IDomainEvent> _raiseEvent = raiseEvent ?? throw new ArgumentNullException( nameof( raiseEvent ) );

    public override void OnCreate() => this.RequestBloom( settings, _raiseEvent );
    public override void OnDestroy() => this.ReleaseBloom( _raiseEvent );
}