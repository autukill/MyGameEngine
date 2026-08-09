namespace MyGame.Runner;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Hosting;

/// <summary>只声明 Spotlight 意图；不持有 Pass、RenderTarget 或其他 GPU 对象。</summary>
public sealed class SpotlightController( StencilMaskGroupRef group, Action<IDomainEvent> raiseEvent, Default2DGameContext context, Vector2D initialCenter, float radius, Action closeWindow )
    : GameInstance {
    public override void OnCreate() {
        Request( initialCenter );
    }

    public override void OnStep( double deltaTime ) {
        if ( context.TryScreenToView( Controls.MousePosition, out ViewportHit hit ) )
            Request( hit.WorldPosition );
    }

    public override void OnDestroy() {
        this.ReleaseStencilMask( group, raiseEvent );
    }

    public override void OnKeyDown( InputKey key ) {
        if ( key == InputKey.Escape ) closeWindow();
    }

    private void Request( Vector2D center ) =>
        this.RequestStencilMask( group, center, radius, StencilMaskState.Spotlight, raiseEvent );
}
