namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;

/// <summary>只声明 Spotlight 意图；不持有 Pass、RenderTarget 或其他 GPU 对象。</summary>
public sealed class SpotlightController( Action<IDomainEvent> raiseEvent, Vector2D initialCenter, float radius, Action closeWindow )
    : GameInstance {
    public override void OnCreate() => Request( initialCenter );

    public override void OnStep( double deltaTime ) {
        if ( Input is null ) return;

        Request( Input.MousePosition );
    }

    public override void OnDestroy() => this.ReleaseStencilMask( raiseEvent );

    public override void OnKeyDown( InputKey key ) {
        if ( key == InputKey.Escape ) closeWindow();
    }

    private void Request( Vector2D center ) =>
        this.RequestStencilMask( center, radius, StencilMaskState.Spotlight, raiseEvent );
}