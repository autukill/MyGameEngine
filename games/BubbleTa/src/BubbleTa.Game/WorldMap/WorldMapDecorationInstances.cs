namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

internal sealed class WorldMapStaticDecorationInstance : GameInstance {
    public WorldMapStaticDecorationInstance( SpriteRef sprite, Vector2D position, int depth ) {
        Sprite = sprite;
        Position = position;
        Depth = new LayerDepth( depth );
        TimeMode = InstanceTimeMode.Unscaled;
    }
}

internal sealed class WorldMapSmokeInstance : GameInstance {
    private const double InitialDelay = 3d;
    private const double ActiveDuration = 3.5d;
    private readonly Vector2D _origin;
    private double _elapsed;

    public WorldMapSmokeInstance( SpriteRef sprite, Vector2D position ) {
        Sprite = sprite;
        _origin = position;
        Position = position;
        Scale = Vector2D.Zero;
        Depth = new LayerDepth( 20 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double cycle = _elapsed % (InitialDelay + ActiveDuration);
        if ( cycle < InitialDelay ) {
            Position = _origin;
            Scale = Vector2D.Zero;
            Color = Vector4.One;
            return;
        }

        float progress = (float)((cycle - InitialDelay) / ActiveDuration);
        Position = new Vector2D( _origin.X, _origin.Y - 322f * progress );
        float scale = 2.4f * progress;
        Scale = new Vector2D( scale, scale );
        Color = new Vector4( 1f, 1f, 1f, 1f - progress );
    }
}

internal sealed class WorldMapMushroomInstance : GameInstance {
    private const double RevealDelay = 24d / 46d;
    private const double SettleDuration = 90d / 46d;
    private double _elapsed;

    public WorldMapMushroomInstance( SpriteRef sprite, Vector2D position ) {
        Sprite = sprite;
        Position = position;
        Scale = Vector2D.Zero;
        Depth = new LayerDepth( 20 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double reveal = _elapsed - RevealDelay;
        if ( reveal < 0d ) return;
        float progress = Math.Clamp( (float)(reveal / SettleDuration), 0f, 1f );
        float wave = MathF.Exp( -5f * progress ) * MathF.Cos( 60f * progress ) * .2f;
        Scale = progress >= 1f
            ? Vector2D.One
            : new Vector2D( 1f + wave, 1f - wave );
    }
}

internal sealed class WorldMapPersonInstance : GameInstance {
    private const double JumpDuration = .5d;
    private const double HoldDuration = 1.5d;
    private const double ReturnDuration = .5d;
    private const double IdleDuration = 5d;
    private readonly WorldMapPersonPlacement _placement;
    private double _elapsed;

    public WorldMapPersonInstance( SpriteRef sprite, in WorldMapPersonPlacement placement ) {
        Sprite = sprite;
        _placement = placement;
        Position = placement.Position;
        Rotation = placement.RotationRadians;
        Depth = new LayerDepth( 20 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double local = _elapsed - _placement.InitialDelaySeconds;
        if ( local < 0d ) return;
        double cycleLength = JumpDuration + HoldDuration + ReturnDuration + IdleDuration;
        double cycle = local % cycleLength;
        Vector2D target = _placement.Position + _placement.JumpOffset;
        if ( cycle < JumpDuration )
            Position = Tween.Lerp( _placement.Position, target, cycle, JumpDuration );
        else if ( cycle < JumpDuration + HoldDuration )
            Position = target;
        else if ( cycle < JumpDuration + HoldDuration + ReturnDuration )
            Position = Tween.Lerp(
                target,
                _placement.Position,
                cycle - JumpDuration - HoldDuration,
                ReturnDuration );
        else
            Position = _placement.Position;
    }
}

internal sealed class WorldMapBirdInstance : GameInstance {
    private const float Speed = 4f * 46f;
    private const double RestDuration = 210d / 46d;
    private readonly GameplayRandom _random;
    private readonly float _baseY;
    private float _direction = 1f;
    private double _restRemaining;

    public bool IsFlying => _restRemaining <= 0d;

    public WorldMapBirdInstance( SpriteRef sprite, Vector2D position, ulong seed ) {
        Sprite = sprite;
        _random = new GameplayRandom( seed );
        _baseY = position.Y;
        Position = new Vector2D( position.X, NextY() );
        ImageSpeed = 1f;
        Depth = new LayerDepth( WorldMapSceneLayout.BirdDepth );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        if ( _restRemaining > 0d ) {
            _restRemaining = Math.Max( 0d, _restRemaining - deltaTime );
            if ( _restRemaining > 0d ) {
                Color = new Vector4( 1f, 1f, 1f, 0f );
                return;
            }
            Position = new Vector2D( Position.X, NextY() );
            Color = Vector4.One;
        }

        Position = new Vector2D(
            Position.X + Speed * _direction * (float)deltaTime,
            Position.Y );
        Scale = new Vector2D( _direction, 1f );
        if ( _direction > 0f && Position.X > WorldMapSceneLayout.RoomBounds.Right + 35f )
            RestAt( WorldMapSceneLayout.RoomBounds.Right + 35f, -1f );
        else if ( _direction < 0f && Position.X < WorldMapSceneLayout.RoomBounds.Left - 35f )
            RestAt( WorldMapSceneLayout.RoomBounds.Left - 35f, 1f );
    }

    private float NextY() => _baseY + _random.Range( -400f, 400f );

    private void RestAt( float x, float nextDirection ) {
        Position = new Vector2D( x, Position.Y );
        _direction = nextDirection;
        _restRemaining = RestDuration;
        Color = new Vector4( 1f, 1f, 1f, 0f );
    }
}

internal sealed class WorldMapLuteaInstance : GameInstance {
    private const float FramesPerSecond = 13.8f;
    private const int FrameCount = 25;
    private readonly SpriteRef _baseSprite;
    private readonly GameplayRandom _random;
    private double _animationElapsed;
    private double _pauseRemaining;

    public bool IsPlaying { get; private set; } = true;

    public WorldMapLuteaInstance(
        SpriteRef baseSprite,
        SpriteRef effectSprite,
        Vector2D position,
        ulong seed ) {
        _baseSprite = baseSprite;
        Sprite = effectSprite;
        _random = new GameplayRandom( seed );
        Position = position;
        ImageSpeed = 0f;
        Depth = new LayerDepth( 20 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        if ( !IsPlaying ) {
            _pauseRemaining -= deltaTime;
            if ( _pauseRemaining > 0d ) return;
            IsPlaying = true;
            _animationElapsed = 0d;
            ImageIndex = 0f;
        }

        _animationElapsed += deltaTime;
        float frame = (float)(_animationElapsed * FramesPerSecond);
        if ( frame < FrameCount ) {
            ImageIndex = frame;
            return;
        }

        IsPlaying = false;
        ImageIndex = FrameCount - 1;
        _pauseRemaining = _random.Range( 1f, 5f );
    }

    public override void OnDraw( ISpriteBatch batch ) {
        Vector2 position = new( Position.X, Position.Y );
        batch.DrawSpriteExt( _baseSprite, 0f, position, Vector2.One, Rotation, Color );
        if ( IsPlaying )
            batch.DrawSpriteExt( Sprite, ImageIndex, position, Vector2.One, Rotation, Color );
    }
}

internal enum WorldMapApplePhase {
    Waiting,
    Reveal,
    Holding,
    Shaking,
    Falling,
    Bouncing,
    Fading
}

internal sealed class WorldMapAppleInstance : GameInstance {
    private const double RevealDuration = .2d;
    private const double ShakeDuration = 28d / 46d;
    private const double FallDuration = .7d;
    private const double BounceDuration = .5d;
    private const double FadeDuration = 1.2d;
    private readonly WorldMapApplePlacement _placement;
    private readonly GameplayRandom _random;
    private double _remaining;
    private double _phaseElapsed;

    public WorldMapApplePhase Phase { get; private set; } = WorldMapApplePhase.Waiting;

    public WorldMapAppleInstance( SpriteRef sprite, in WorldMapApplePlacement placement ) {
        Sprite = sprite;
        _placement = placement;
        _random = new GameplayRandom( placement.Seed );
        Position = placement.Position;
        Scale = Vector2D.Zero;
        Depth = new LayerDepth( 20 );
        TimeMode = InstanceTimeMode.Unscaled;
        _remaining = placement.InitialDelaySeconds;
    }

    public override void OnStep( double deltaTime ) {
        _phaseElapsed += deltaTime;
        _remaining -= deltaTime;
        switch ( Phase ) {
            case WorldMapApplePhase.Waiting:
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Reveal, RevealDuration );
                break;
            case WorldMapApplePhase.Reveal:
                ApplyReveal();
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Holding, _random.Range( 3f, 5f ) );
                break;
            case WorldMapApplePhase.Holding:
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Shaking, ShakeDuration );
                break;
            case WorldMapApplePhase.Shaking:
                Rotation = MathF.Sin( (float)(_phaseElapsed * 46d / 4d * Math.Tau) ) * MathF.PI / 10f;
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Falling, FallDuration );
                break;
            case WorldMapApplePhase.Falling:
                ApplyFall();
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Bouncing, BounceDuration );
                break;
            case WorldMapApplePhase.Bouncing:
                ApplyBounce();
                if ( _remaining <= 0d ) Begin( WorldMapApplePhase.Fading, FadeDuration );
                break;
            case WorldMapApplePhase.Fading:
                Color = new Vector4( 1f, 1f, 1f,
                    1f - Math.Clamp( (float)(_phaseElapsed / FadeDuration), 0f, 1f ) );
                if ( _remaining <= 0d ) Reset();
                break;
        }
    }

    private void ApplyReveal() {
        float scale = Tween.Lerp( 0f, 1f, _phaseElapsed, RevealDuration, EasingKind.BackOut );
        Scale = new Vector2D( scale, scale );
    }

    private void ApplyFall() {
        float progress = Easing.Evaluate(
            EasingKind.QuadIn,
            Math.Clamp( _phaseElapsed / FallDuration, 0d, 1d ) );
        Position = new Vector2D(
            _placement.Position.X,
            _placement.Position.Y + (_placement.EndY - _placement.Position.Y) * progress );
    }

    private void ApplyBounce() {
        float progress = Math.Clamp( (float)(_phaseElapsed / BounceDuration), 0f, 1f );
        Position = new Vector2D(
            _placement.Position.X,
            _placement.EndY - MathF.Sin( progress * MathF.PI ) * 24f );
        Scale = new Vector2D(
            Tween.Lerp( .8f, 1f, progress ),
            Tween.Lerp( 1.2f, 1f, progress ) );
    }

    private void Begin( WorldMapApplePhase phase, double duration ) {
        Phase = phase;
        _phaseElapsed = 0d;
        _remaining = duration;
        if ( phase == WorldMapApplePhase.Reveal ) {
            Position = _placement.Position;
            Scale = Vector2D.Zero;
            Color = Vector4.One;
            Rotation = 0f;
        }
        if ( phase == WorldMapApplePhase.Falling ) Rotation = 0f;
    }

    private void Reset() {
        Phase = WorldMapApplePhase.Waiting;
        _phaseElapsed = 0d;
        _remaining = _random.Range( 2f, 5f );
        Position = _placement.Position;
        Scale = Vector2D.Zero;
        Color = Vector4.One;
        Rotation = 0f;
    }
}
