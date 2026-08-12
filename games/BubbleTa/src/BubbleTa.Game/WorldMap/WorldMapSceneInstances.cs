namespace BubbleTa.Game.WorldMap;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

internal sealed class WorldMapIslandInstance : GameInstance {
    public WorldMapIslandInstance( SpriteRef sprite, Vector2D position ) {
        Sprite = sprite;
        Position = position;
        Scale = new Vector2D( 2f, 2f );
        Depth = new LayerDepth( 100 );
        TimeMode = InstanceTimeMode.Unscaled;
    }
}

internal sealed class WorldMapCloudInstance : GameInstance {
    private const double CycleSeconds = 7d;
    private readonly Vector2D _origin;
    private readonly float _phase;
    private double _elapsed;

    public WorldMapCloudInstance(
        SpriteRef sprite,
        in WorldMapCloudPlacement placement ) {
        Sprite = sprite;
        ImageIndex = placement.SubImage;
        _origin = placement.Position;
        _phase = placement.Phase;
        Position = _origin;
        Scale = new Vector2D( placement.Scale, placement.Scale );
        Depth = new LayerDepth( placement.Depth );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        double angle = _phase + _elapsed / CycleSeconds * Math.Tau;
        Position = new Vector2D(
            _origin.X + 10f * (float)Math.Cos( angle ),
            _origin.Y + 4f * (float)Math.Sin( angle ) );
    }
}

internal sealed class WorldMapLevelNodeInstance : GameInstance {
    private const float HalfWidth = 45f;
    private const float HalfHeight = 34.5f;
    private const float DragCancelDistanceSquared = 64f;
    private readonly Func<Vector2D, Vector2D?> _screenToWorld;
    private readonly Action<WorldMapLevelSelectionRequested> _requestSelection;
    private bool _previousDown;
    private bool _captured;
    private Vector2D _pressScreenPosition;
    private double _elapsed;
    private double _selectionPulseRemaining;

    public int Level { get; }
    public WorldMapNodeKind Kind { get; }
    public WorldMapLevelState State { get; }
    public int Stars { get; }
    public bool IsLocked => State == WorldMapLevelState.Locked;
    public bool IsCaptured => _captured;
    public bool WasSelected { get; private set; }

    public WorldMapLevelNodeInstance(
        SpriteRef sprite,
        in WorldMapNodePlacement placement,
        WorldMapLevelState state,
        int stars,
        Func<Vector2D, Vector2D?> screenToWorld,
        Action<WorldMapLevelSelectionRequested> requestSelection ) {
        if ( stars is < 0 or > 3 )
            throw new ArgumentOutOfRangeException( nameof( stars ) );
        if ( state != WorldMapLevelState.Completed && stars != 0 )
            throw new ArgumentException(
                "Only completed WorldMap nodes can display completion stars.",
                nameof( stars ) );
        Sprite = sprite;
        Level = placement.Level;
        Kind = placement.Kind;
        State = state;
        Stars = stars;
        _screenToWorld = screenToWorld ?? throw new ArgumentNullException( nameof( screenToWorld ) );
        _requestSelection = requestSelection ?? throw new ArgumentNullException( nameof( requestSelection ) );
        Position = placement.Position;
        Color = state == WorldMapLevelState.Completed
            ? new System.Numerics.Vector4( .82f, .82f, .82f, 1f )
            : System.Numerics.Vector4.One;
        Depth = new LayerDepth( 0 );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        _elapsed += deltaTime;
        _selectionPulseRemaining = Math.Max( 0d, _selectionPulseRemaining - deltaTime );
        Vector2D screen = Controls.MousePosition;
        UpdatePointer(
            _screenToWorld( screen ),
            screen,
            Controls.IsMouseButtonDown( MouseButton.Left ) );
    }

    internal void UpdatePointer( Vector2D? world, Vector2D screen, bool down ) {
        bool inside = world is { } point && Contains( point );
        bool pressed = down && !_previousDown;
        bool released = !down && _previousDown;

        if ( pressed ) {
            _captured = !IsLocked && inside;
            _pressScreenPosition = screen;
        }
        if ( _captured && down ) {
            Vector2D movement = screen - _pressScreenPosition;
            if ( movement.X * movement.X + movement.Y * movement.Y > DragCancelDistanceSquared )
                _captured = false;
        }
        if ( released ) {
            if ( _captured && inside ) {
                WasSelected = true;
                _selectionPulseRemaining = .2d;
                _requestSelection( new WorldMapLevelSelectionRequested( Level, Kind, State ) );
            }
            _captured = false;
        }
        _previousDown = down;

        float scale = State == WorldMapLevelState.Available
            ? .97f + .03f * MathF.Sin( (float)(_elapsed * Math.Tau / 1.2d) )
            : 1f;
        if ( _selectionPulseRemaining > 0d ) scale *= 1.1f;
        else if ( _captured && down ) scale *= .94f;
        else if ( !IsLocked && inside ) scale *= 1.05f;
        Scale = new Vector2D( scale, scale );
    }

    private bool Contains( Vector2D point ) =>
        point.X >= Position.X - HalfWidth && point.X <= Position.X + HalfWidth &&
        point.Y >= Position.Y - HalfHeight && point.Y <= Position.Y + HalfHeight;
}

internal sealed class WorldMapController( Action returnHome ) : GameInstance {
    public WorldMapLevelSelectionRequested? LastSelection { get; private set; }

    public void RequestSelection( WorldMapLevelSelectionRequested request ) =>
        LastSelection = request;

    public override void OnKeyDown( InputKey key ) {
        if ( key == InputKey.Escape ) returnHome();
    }
}

internal sealed class WorldMapSmokeProbe( Action close ) : GameInstance {
    private int _steps;

    public override void OnStep( double deltaTime ) {
        _steps++;
        if ( _steps < 3 ) return;

        RequireCount<WorldMapIslandInstance>( 2 );
        RequireCount<WorldMapCloudInstance>(
            WorldMapSceneLayout.UnderClouds.Length + WorldMapSceneLayout.AboveClouds.Length );
        RequireCount<WorldMapLevelNodeInstance>( 20 );
        RequireCount<WorldMapSmokeInstance>( 1 );
        RequireCount<WorldMapStaticDecorationInstance>( 1 );
        RequireCount<WorldMapMushroomInstance>( 1 );
        RequireCount<WorldMapPersonInstance>( 3 );
        RequireCount<WorldMapBirdInstance>( 1 );
        RequireCount<WorldMapLuteaInstance>( 1 );
        RequireCount<WorldMapAppleInstance>( 3 );
        RequireCount<WorldMapSegmentVisibilityController>( 1 );
        RequireCount<WorldMapSkyTransitionController>( 1 );
        close();
    }

    private void RequireCount<T>( int expected ) where T : GameInstance {
        int actual = CountInstances<T>();
        if ( actual != expected )
            throw new InvalidOperationException(
                $"BubbleTa WorldMap smoke expected {expected} {typeof(T).Name} instances, got {actual}." );
    }
}
