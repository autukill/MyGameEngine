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
    public int Level { get; }
    public WorldMapNodeKind Kind { get; }
    public bool IsLocked { get; }

    public WorldMapLevelNodeInstance(
        SpriteRef sprite,
        in WorldMapNodePlacement placement,
        bool locked ) {
        Sprite = sprite;
        Level = placement.Level;
        Kind = placement.Kind;
        IsLocked = locked;
        Position = placement.Position;
        Depth = new LayerDepth( 0 );
        TimeMode = InstanceTimeMode.Unscaled;
    }
}

internal sealed class WorldMapController( Action returnHome ) : GameInstance {
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
        close();
    }

    private void RequireCount<T>( int expected ) where T : GameInstance {
        int actual = CountInstances<T>();
        if ( actual != expected )
            throw new InvalidOperationException(
                $"BubbleTa WorldMap smoke expected {expected} {typeof(T).Name} instances, got {actual}." );
    }
}
