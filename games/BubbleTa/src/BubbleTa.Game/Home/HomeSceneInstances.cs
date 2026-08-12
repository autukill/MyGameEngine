namespace BubbleTa.Game.Home;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

internal sealed class StaticHomeSpriteInstance : GameInstance {
    public StaticHomeSpriteInstance(
        SpriteRef sprite,
        Vector2D position,
        Vector2D scale,
        int depth ) {
        Sprite = sprite;
        Position = position;
        Scale = scale;
        Depth = new LayerDepth( depth );
        TimeMode = InstanceTimeMode.Unscaled;
    }
}

internal sealed class LogoRevealInstance : GameInstance {
    private const double ScaleDuration = .2d;
    private const double DropScaleDuration = .4d;
    private readonly LogoPlacement _placement;
    private double _elapsed;

    public double ElapsedSeconds => _elapsed;
    public bool IsRevealed => _elapsed >= _placement.DelaySeconds;

    public LogoRevealInstance( SpriteRef sprite, in LogoPlacement placement ) {
        Sprite = sprite;
        _placement = placement;
        Position = placement.Position;
        Depth = new LayerDepth( placement.Depth );
        TimeMode = InstanceTimeMode.Unscaled;
        if ( placement.Reveal == LogoRevealKind.Alpha ) {
            Scale = Vector2D.One;
            Color = new Vector4( 1f, 1f, 1f, 0f );
        }
        else {
            Scale = Vector2D.Zero;
        }
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double revealElapsed = _elapsed - _placement.DelaySeconds;
        if ( revealElapsed < 0d ) return;

        switch (_placement.Reveal) {
            case LogoRevealKind.Scale:
            {
                float scale = Tween.Lerp(
                    0f, 1f, revealElapsed, ScaleDuration, EasingKind.BackOut );
                Scale = new Vector2D( scale, scale );
                break;
            }
            case LogoRevealKind.Alpha:
            {
                float alpha = Tween.Lerp(
                    0f, 1f, revealElapsed, ScaleDuration, EasingKind.Linear );
                Color = new Vector4( 1f, 1f, 1f, alpha );
                break;
            }
            case LogoRevealKind.DropScale:
            {
                float scale = Tween.Lerp(
                    0f, 1f, revealElapsed, DropScaleDuration, EasingKind.BackOut );
                float y = Tween.Lerp(
                    _placement.Position.Y + 40f,
                    _placement.Position.Y,
                    revealElapsed,
                    ScaleDuration,
                    EasingKind.Linear );
                Position = new Vector2D( _placement.Position.X, y );
                Scale = new Vector2D( scale, scale );
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Unsupported Logo reveal kind '{_placement.Reveal}'." );
        }
    }
}

internal sealed class HomeStarInstance : GameInstance {
    private const double OneWayDuration = .45d;
    private readonly Vector2D _baseScale;
    private double _elapsed;

    public HomeStarInstance( SpriteRef sprite, in SpritePlacement placement ) {
        Sprite = sprite;
        Position = placement.Position;
        _baseScale = placement.Scale;
        Scale = new Vector2D( _baseScale.X + .2f, _baseScale.X + .2f );
        Depth = new LayerDepth( placement.Depth );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double phase = HomeAnimationMath.PingPong( _elapsed, OneWayDuration );
        float scale = Tween.Lerp( _baseScale.X + .2f, _baseScale.X, phase );
        Scale = new Vector2D( scale, scale );
    }
}

internal sealed class HomeBubbleInstance : GameInstance {
    private const double RevealDelay = .8d;
    private const double RevealDuration = .15d;
    private double _elapsed;

    public HomeBubbleInstance( SpriteRef sprite, Vector2D position ) {
        Sprite = sprite;
        Position = position;
        Scale = Vector2D.Zero;
        Depth = new LayerDepth( -1 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double revealElapsed = _elapsed - RevealDelay;
        if ( revealElapsed < 0d ) return;

        double phase = HomeAnimationMath.CharacterBobPhase( _elapsed );
        float pulse = Tween.Lerp( 1.02f, 1f, phase );
        if ( revealElapsed <= RevealDuration ) {
            float reveal = Tween.Lerp( 0f, 1f, revealElapsed, RevealDuration );
            float scale = pulse * reveal;
            Scale = new Vector2D( scale, scale );
            return;
        }

        Scale = new Vector2D( pulse, pulse );
    }
}

internal sealed class HomeCloudInstance : GameInstance {
    private const double SlideDuration = 2d;
    private const double BobOneWayDuration = 1.2d;
    private readonly Vector2D _target;
    private double _elapsed;

    public HomeCloudInstance( SpriteRef sprite, Vector2D target ) {
        Sprite = sprite;
        _target = target;
        Position = new Vector2D( -224f, target.Y );
        Scale = new Vector2D( 2f, 2f );
        Depth = new LayerDepth( -9 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        if ( _elapsed <= SlideDuration ) {
            float x = Tween.Lerp( -224f, _target.X, _elapsed, SlideDuration );
            Position = new Vector2D( x, _target.Y );
            return;
        }

        double phase = HomeAnimationMath.PingPong(
            _elapsed - SlideDuration,
            BobOneWayDuration );
        float y = Tween.Lerp( _target.Y, _target.Y + 20f, phase );
        Position = new Vector2D( _target.X, y );
    }
}

internal sealed class HomeSpotInstance : GameInstance {
    private const double FadeDuration = .4d;
    private const double RestoreAfter = 1d;
    private readonly GameplayRandom _random;
    private double _untilFade;
    private double _fadeElapsed;
    private bool _fading;

    public double NextFadeSeconds => _untilFade;
    public bool IsFading => _fading;

    public HomeSpotInstance( SpriteRef sprite, Vector2D position, ulong seed ) {
        Sprite = sprite;
        Position = position;
        Depth = new LayerDepth( 0 );
        TimeMode = InstanceTimeMode.Unscaled;
        _random = new GameplayRandom( seed );
        _untilFade = NextDelay();
    }

    public override void OnStep( double deltaTime ) {
        if ( !_fading ) {
            _untilFade -= deltaTime;
            if ( _untilFade > 0d ) return;

            _fading = true;
            _fadeElapsed = 0d;
        }

        _fadeElapsed += deltaTime;
        if ( _fadeElapsed >= RestoreAfter ) {
            _fading = false;
            _untilFade = NextDelay();
            Color = Vector4.One;
            return;
        }

        float alpha = Tween.Lerp( 1f, 0f, _fadeElapsed, FadeDuration );
        Color = new Vector4( 1f, 1f, 1f, alpha );
    }

    private double NextDelay() => _random.Range( 3f, 6f );
}

internal sealed class HomeMeteorInstance : GameInstance {
    private const float PixelsPerSecond = 40f * (float)HomeSceneLayout.LegacyFramesPerSecond;

    private static readonly Vector2D VelocityValue = new(
        MathF.Cos( 210f * MathF.PI / 180f ) * PixelsPerSecond,
        -MathF.Sin( 210f * MathF.PI / 180f ) * PixelsPerSecond);

    private readonly Vector2D _start;
    private readonly GameplayRandom _random;
    private double _untilReset;
    private bool _moving;

    public static Vector2D Velocity => VelocityValue;
    public double NextResetSeconds => _untilReset;
    public bool IsMoving => _moving;

    public HomeMeteorInstance( SpriteRef sprite, Vector2D start, ulong seed ) {
        Sprite = sprite;
        _start = start;
        Position = start;
        Rotation = MathF.PI / 6f;
        Depth = new LayerDepth( 99 );
        TimeMode = InstanceTimeMode.Unscaled;
        _random = new GameplayRandom( seed );
        _untilReset = _random.Range( 1f, 6f );
    }

    public override void OnStep( double deltaTime ) {
        _untilReset -= deltaTime;
        if ( _untilReset <= 0d ) {
            Position = _start;
            _moving = true;
            _untilReset += _random.Range( 3f, 6f );
        }

        if ( _moving ) Position += VelocityValue * (float)deltaTime;
    }
}

internal sealed class HomeCharacterInstance : GameInstance {
    private const double EntranceDuration = .15d;

    private readonly SpriteRef _baseSprite;
    private readonly bool _drawOverlay;
    private readonly Vector2D _start;
    private readonly Vector2D _target;
    private readonly float _startScale;
    private readonly float _targetScale;
    private readonly float _bobOffset;
    private readonly double _delay;
    private double _elapsed;

    public double ElapsedSeconds => _elapsed;
    public double EntranceDelaySeconds => _delay;

    private HomeCharacterInstance(
        SpriteRef sprite,
        SpriteRef baseSprite,
        bool drawOverlay,
        Vector2D start,
        Vector2D target,
        float startScale,
        float targetScale,
        float bobOffset,
        double delay,
        int depth ) {
        Sprite = sprite;
        _baseSprite = baseSprite;
        _drawOverlay = drawOverlay;
        _start = start;
        _target = target;
        _startScale = startScale;
        _targetScale = targetScale;
        _bobOffset = bobOffset;
        _delay = delay;
        Position = start;
        Scale = new Vector2D( startScale, startScale );
        Depth = new LayerDepth( depth );
        TimeMode = InstanceTimeMode.Unscaled;
        if ( drawOverlay ) {
            Color = new Vector4( 1f, 1f, 1f, 0f );
            ImageSpeed = 0f;
            ViewCulling = InstanceViewCullingMode.AlwaysVisible;
        }
    }

    public static HomeCharacterInstance CreateHero( SpriteRef baseSprite, SpriteRef effectSprite ) =>
        new(
            effectSprite,
            baseSprite,
            true,
            new Vector2D( HomeSceneLayout.HeroPosition.X, 910f ),
            HomeSceneLayout.HeroPosition,
            1f,
            1f,
            -20f,
            .6d,
            -4);

    public static HomeCharacterInstance CreateSnow( SpriteRef sprite ) =>
        new(
            sprite,
            SpriteRef.Empty,
            false,
            new Vector2D( 540f, 890f ),
            HomeSceneLayout.SnowPosition,
            0f,
            1f,
            20f,
            .2d,
            -6);

    public static HomeCharacterInstance CreateKing( SpriteRef sprite ) =>
        new(
            sprite,
            SpriteRef.Empty,
            false,
            new Vector2D( 540f, 890f ),
            HomeSceneLayout.KingPosition,
            0f,
            1f,
            -20f,
            .4d,
            -5);

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double entranceElapsed = _elapsed - _delay;
        if ( entranceElapsed < 0d ) return;

        double entranceProgress = Math.Clamp( entranceElapsed / EntranceDuration, 0d, 1d );
        Position = Tween.Lerp( _start, _target, entranceProgress );
        if ( _drawOverlay ) {
            Color = Vector4.One;
        }
        else {
            float scale = Tween.Lerp(
                _startScale,
                _targetScale,
                entranceProgress,
                EasingKind.BackOut );
            Scale = new Vector2D( scale, scale );
        }

        if ( _elapsed < HomeAnimationMath.CharacterIdleStart ) return;

        double phase = HomeAnimationMath.CharacterBobPhase( _elapsed );
        Position = new Vector2D(
            _target.X,
            Tween.Lerp( _target.Y, _target.Y + _bobOffset, phase ) );
        if ( _drawOverlay ) ImageSpeed = 1f;
    }

    public override void OnDraw( ISpriteBatch batch ) {
        if ( !_drawOverlay ) {
            base.OnDraw( batch );
            return;
        }

        Vector2 position = new(Position.X, Position.Y);
        batch.DrawSpriteExt(
            _baseSprite,
            0f,
            position,
            Vector2.One,
            0f,
            Color );
        batch.DrawSpriteExt(
            Sprite,
            ImageIndex,
            position,
            new Vector2( 2f, 2f ),
            0f,
            Color );
    }
}

internal sealed class HomeWorldButtonInstance : GameInstance {
    private const double RevealDelay = 1d;
    private const double RevealDuration = .2d;
    private const double IdleOneWayDuration = .8d;
    private const float HalfWidth = 100.5f;
    private const float HalfHeight = 100.5f;

    private readonly Func<Vector2D, Vector2D?> _screenToWorld;
    private readonly Action _activate;
    private double _elapsed;
    private bool _previousDown;
    private bool _captured;
    private bool _activated;

    public bool IsRevealed => _elapsed >= RevealDelay + RevealDuration;
    public bool IsCaptured => _captured;
    public bool WasActivated => _activated;

    public HomeWorldButtonInstance(
        SpriteRef sprite,
        Func<Vector2D, Vector2D?> screenToWorld,
        Action activate ) {
        Sprite = sprite;
        _screenToWorld = screenToWorld ?? throw new ArgumentNullException( nameof( screenToWorld ) );
        _activate = activate ?? throw new ArgumentNullException( nameof( activate ) );
        Position = HomeSceneLayout.WorldButtonPosition;
        Scale = Vector2D.Zero;
        Depth = new LayerDepth( -10 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        Vector2D? pointer = _screenToWorld( Controls.MousePosition );
        bool down = Controls.IsMouseButtonDown( MouseButton.Left );
        UpdatePointer( pointer, down );
    }

    internal void UpdatePointer( Vector2D? pointer, bool down ) {
        bool inside = pointer is { } world && Contains( world );
        bool pressed = down && !_previousDown;
        bool released = !down && _previousDown;
        bool interactive = _elapsed >= RevealDelay + RevealDuration;

        if ( pressed ) _captured = interactive && inside;
        if ( released ) {
            if ( !_activated && _captured && inside ) {
                _activated = true;
                _activate();
            }

            _captured = false;
        }

        _previousDown = down;

        if ( _elapsed < RevealDelay ) {
            Scale = Vector2D.Zero;
            return;
        }

        if ( _elapsed < RevealDelay + RevealDuration ) {
            float reveal = Tween.Lerp(
                0f,
                1f,
                _elapsed - RevealDelay,
                RevealDuration,
                EasingKind.BackOut );
            Scale = new Vector2D( reveal, reveal );
            return;
        }

        double idle = HomeAnimationMath.PingPong(
            _elapsed - RevealDelay - RevealDuration,
            IdleOneWayDuration );
        float scale = Tween.Lerp( 1f, .96f, idle, EasingKind.SineInOut );
        if ( _captured && down ) scale *= .96f;
        else if ( inside ) scale *= 1.06f;
        Scale = new Vector2D( scale, scale );
    }

    private bool Contains( Vector2D point ) =>
        point.X >= Position.X - HalfWidth && point.X <= Position.X + HalfWidth &&
        point.Y >= Position.Y - HalfHeight && point.Y <= Position.Y + HalfHeight;
}

internal sealed class HomeSceneController( Action close ) : GameInstance {
    public override void OnKeyDown( InputKey key ) {
        if ( key == InputKey.Escape ) close();
    }
}

internal sealed class HomeSmokeProbe( Action switchToWorldMap ) : GameInstance {
    private int _steps;

    public override void OnStep( double deltaTime ) {
        _steps++;
        if ( _steps == 180 ) {
            RequireCount<LogoRevealInstance>( 12 );
            RequireCount<HomeMeteorInstance>( 5 );
            RequireCount<HomeSpotInstance>( 5 );
            RequireCount<HomeStarInstance>( 3 );
            RequireCount<HomeCharacterInstance>( 3 );
            RequireCount<HomeWorldButtonInstance>( 1 );
        }

        if ( _steps == 181 ) switchToWorldMap();
    }

    private void RequireCount<T>( int expected ) where T : GameInstance {
        int actual = CountInstances<T>();
        if ( actual != expected )
            throw new InvalidOperationException(
                $"BubbleTa Home smoke expected {expected} {typeof(T).Name} instances, got {actual}." );
    }
}
