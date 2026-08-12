namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;

internal enum WorldMapSegmentTheme {
    ForestVillage,
    Grassland,
    DesertOasis,
    SnowCastle,
    SummitForest
}

internal sealed class WorldMapSegmentDefinition {
    private readonly WorldMapNodePlacement[] _nodes;

    public int Id { get; }
    public WorldMapSegmentTheme Theme { get; }
    public Bounds2D Bounds { get; }
    public int HorizontalDirection { get; }
    public Vector4 SkyColor { get; }
    public int FirstLevel => _nodes[0].Level;
    public int LastLevel => _nodes[^1].Level;
    public ReadOnlySpan<WorldMapNodePlacement> Nodes => _nodes;

    public WorldMapSegmentDefinition(
        int id,
        WorldMapSegmentTheme theme,
        Bounds2D bounds,
        int horizontalDirection,
        Vector4 skyColor,
        ReadOnlySpan<WorldMapNodePlacement> nodes ) {
        if ( id is < 0 or > 4 ) throw new ArgumentOutOfRangeException( nameof( id ) );
        if ( !Enum.IsDefined( theme ) ) throw new ArgumentOutOfRangeException( nameof( theme ) );
        if ( horizontalDirection is < -1 or > 1 )
            throw new ArgumentOutOfRangeException( nameof( horizontalDirection ) );
        if ( nodes.Length != 20 )
            throw new ArgumentException( "A BubbleTa WorldMap segment must contain twenty nodes.", nameof( nodes ) );
        int expectedLevel = id * 20 + 1;
        for (int i = 0; i < nodes.Length; i++) {
            if ( nodes[i].Level != expectedLevel + i )
                throw new ArgumentException(
                    $"Segment {id} node {i} must be level {expectedLevel + i}.",
                    nameof( nodes ) );
            if ( !bounds.Contains( nodes[i].Position ) )
                throw new ArgumentException(
                    $"Level {nodes[i].Level} lies outside segment {id} bounds.",
                    nameof( nodes ) );
        }

        Id = id;
        Theme = theme;
        Bounds = bounds;
        HorizontalDirection = horizontalDirection;
        SkyColor = skyColor;
        _nodes = nodes.ToArray();
    }
}

internal static class WorldMapSegmentCatalog {
    private static readonly WorldMapNodePlacement[] GrasslandNodes = [
        new( 21, new Vector2D( 603f, 12_274f ), WorldMapNodeKind.Timed ),
        new( 22, new Vector2D( 425f, 12_249f ), WorldMapNodeKind.Normal ),
        new( 23, new Vector2D( 315f, 12_143f ), WorldMapNodeKind.Moving ),
        new( 24, new Vector2D( 401f, 12_040f ), WorldMapNodeKind.Normal ),
        new( 25, new Vector2D( 543f, 11_978f ), WorldMapNodeKind.Timed ),
        new( 26, new Vector2D( 658f, 11_891f ), WorldMapNodeKind.Normal ),
        new( 27, new Vector2D( 560f, 11_768f ), WorldMapNodeKind.Moving ),
        new( 28, new Vector2D( 432f, 11_699f ), WorldMapNodeKind.Normal ),
        new( 29, new Vector2D( 553f, 11_613f ), WorldMapNodeKind.Normal ),
        new( 30, new Vector2D( 687f, 11_518f ), WorldMapNodeKind.Normal ),
        new( 31, new Vector2D( 531f, 11_444f ), WorldMapNodeKind.Normal ),
        new( 32, new Vector2D( 387f, 11_400f ), WorldMapNodeKind.Moving ),
        new( 33, new Vector2D( 353f, 11_255f ), WorldMapNodeKind.Normal ),
        new( 34, new Vector2D( 526f, 11_159f ), WorldMapNodeKind.Timed ),
        new( 35, new Vector2D( 684f, 11_106f ), WorldMapNodeKind.Moving ),
        new( 36, new Vector2D( 652f, 10_994f ), WorldMapNodeKind.Normal ),
        new( 37, new Vector2D( 506f, 10_907f ), WorldMapNodeKind.Normal ),
        new( 38, new Vector2D( 519f, 10_765f ), WorldMapNodeKind.Moving ),
        new( 39, new Vector2D( 552f, 10_632f ), WorldMapNodeKind.Normal ),
        new( 40, new Vector2D( 657f, 10_539f ), WorldMapNodeKind.Normal )
    ];

    private static readonly WorldMapNodePlacement[] DesertNodes = [
        new( 41, new Vector2D( 385f, 9_097f ), WorldMapNodeKind.Timed ),
        new( 42, new Vector2D( 541f, 9_087f ), WorldMapNodeKind.Normal ),
        new( 43, new Vector2D( 691f, 9_019f ), WorldMapNodeKind.Moving ),
        new( 44, new Vector2D( 646f, 8_887f ), WorldMapNodeKind.Normal ),
        new( 45, new Vector2D( 515f, 8_779f ), WorldMapNodeKind.Normal ),
        new( 46, new Vector2D( 401f, 8_677f ), WorldMapNodeKind.Timed ),
        new( 47, new Vector2D( 371f, 8_541f ), WorldMapNodeKind.Normal ),
        new( 48, new Vector2D( 461f, 8_417f ), WorldMapNodeKind.Timed ),
        new( 49, new Vector2D( 589f, 8_331f ), WorldMapNodeKind.Normal ),
        new( 50, new Vector2D( 662f, 8_215f ), WorldMapNodeKind.Normal ),
        new( 51, new Vector2D( 605f, 8_075f ), WorldMapNodeKind.Moving ),
        new( 52, new Vector2D( 456f, 7_986f ), WorldMapNodeKind.Timed ),
        new( 53, new Vector2D( 367f, 7_872f ), WorldMapNodeKind.Normal ),
        new( 54, new Vector2D( 472f, 7_755f ), WorldMapNodeKind.Normal ),
        new( 55, new Vector2D( 628f, 7_655f ), WorldMapNodeKind.Timed ),
        new( 56, new Vector2D( 756f, 7_585f ), WorldMapNodeKind.Normal ),
        new( 57, new Vector2D( 636f, 7_484f ), WorldMapNodeKind.Normal ),
        new( 58, new Vector2D( 684f, 7_352f ), WorldMapNodeKind.Moving ),
        new( 59, new Vector2D( 572f, 7_272f ), WorldMapNodeKind.Moving ),
        new( 60, new Vector2D( 539f, 7_156f ), WorldMapNodeKind.Normal )
    ];

    private static readonly WorldMapNodePlacement[] SnowNodes = [
        new( 61, new Vector2D( 481f, 5_897f ), WorldMapNodeKind.Normal ),
        new( 62, new Vector2D( 629f, 5_882f ), WorldMapNodeKind.Normal ),
        new( 63, new Vector2D( 726f, 5_801f ), WorldMapNodeKind.Moving ),
        new( 64, new Vector2D( 657f, 5_664f ), WorldMapNodeKind.Normal ),
        new( 65, new Vector2D( 536f, 5_564f ), WorldMapNodeKind.Timed ),
        new( 66, new Vector2D( 451f, 5_458f ), WorldMapNodeKind.Normal ),
        new( 67, new Vector2D( 531f, 5_339f ), WorldMapNodeKind.Normal ),
        new( 68, new Vector2D( 679f, 5_264f ), WorldMapNodeKind.Normal ),
        new( 69, new Vector2D( 683f, 5_132f ), WorldMapNodeKind.Timed ),
        new( 70, new Vector2D( 527f, 5_072f ), WorldMapNodeKind.Moving ),
        new( 71, new Vector2D( 400f, 4_997f ), WorldMapNodeKind.Normal ),
        new( 72, new Vector2D( 345f, 4_874f ), WorldMapNodeKind.Normal ),
        new( 73, new Vector2D( 454f, 4_757f ), WorldMapNodeKind.Normal ),
        new( 74, new Vector2D( 543f, 4_640f ), WorldMapNodeKind.Normal ),
        new( 75, new Vector2D( 536f, 4_494f ), WorldMapNodeKind.Moving ),
        new( 76, new Vector2D( 593f, 4_359f ), WorldMapNodeKind.Normal ),
        new( 77, new Vector2D( 590f, 4_222f ), WorldMapNodeKind.Normal ),
        new( 78, new Vector2D( 462f, 4_146f ), WorldMapNodeKind.Timed ),
        new( 79, new Vector2D( 452f, 4_030f ), WorldMapNodeKind.Moving ),
        new( 80, new Vector2D( 583f, 3_986f ), WorldMapNodeKind.Normal )
    ];

    private static readonly WorldMapNodePlacement[] SummitNodes = [
        new( 81, new Vector2D( 511f, 2_795f ), WorldMapNodeKind.Normal ),
        new( 82, new Vector2D( 650f, 2_763f ), WorldMapNodeKind.Moving ),
        new( 83, new Vector2D( 758f, 2_677f ), WorldMapNodeKind.Normal ),
        new( 84, new Vector2D( 739f, 2_556f ), WorldMapNodeKind.Normal ),
        new( 85, new Vector2D( 630f, 2_475f ), WorldMapNodeKind.Timed ),
        new( 86, new Vector2D( 491f, 2_411f ), WorldMapNodeKind.Normal ),
        new( 87, new Vector2D( 381f, 2_325f ), WorldMapNodeKind.Normal ),
        new( 88, new Vector2D( 332f, 2_212f ), WorldMapNodeKind.Timed ),
        new( 89, new Vector2D( 391f, 2_091f ), WorldMapNodeKind.Moving ),
        new( 90, new Vector2D( 531f, 2_034f ), WorldMapNodeKind.Timed ),
        new( 91, new Vector2D( 657f, 1_979f ), WorldMapNodeKind.Normal ),
        new( 92, new Vector2D( 739f, 1_900f ), WorldMapNodeKind.Normal ),
        new( 93, new Vector2D( 716f, 1_761f ), WorldMapNodeKind.Normal ),
        new( 94, new Vector2D( 616f, 1_633f ), WorldMapNodeKind.Moving ),
        new( 95, new Vector2D( 487f, 1_541f ), WorldMapNodeKind.Timed ),
        new( 96, new Vector2D( 396f, 1_441f ), WorldMapNodeKind.Timed ),
        new( 97, new Vector2D( 435f, 1_276f ), WorldMapNodeKind.Normal ),
        new( 98, new Vector2D( 597f, 1_143f ), WorldMapNodeKind.Normal ),
        new( 99, new Vector2D( 533f, 984f ), WorldMapNodeKind.Normal ),
        new( 100, new Vector2D( 460f, 878f ), WorldMapNodeKind.Normal )
    ];

    private static readonly WorldMapSegmentDefinition[] SegmentData = [
        new( 0, WorldMapSegmentTheme.ForestVillage,
            new Bounds2D( 0f, 13_300f, 1_048f, 15_972f ), 0,
            Rgb( 108, 128, 223 ), WorldMapSceneLayout.FirstIslandNodes ),
        new( 1, WorldMapSegmentTheme.Grassland,
            new Bounds2D( 0f, 10_050f, 1_048f, 12_722f ), 1,
            Rgb( 81, 151, 218 ), GrasslandNodes ),
        new( 2, WorldMapSegmentTheme.DesertOasis,
            new Bounds2D( 0f, 6_800f, 1_048f, 9_472f ), -1,
            Rgb( 141, 64, 90 ), DesertNodes ),
        new( 3, WorldMapSegmentTheme.SnowCastle,
            new Bounds2D( 0f, 3_550f, 1_048f, 6_222f ), 1,
            Rgb( 141, 88, 170 ), SnowNodes ),
        new( 4, WorldMapSegmentTheme.SummitForest,
            new Bounds2D( 0f, 550f, 1_048f, 3_222f ), -1,
            Rgb( 108, 128, 223 ), SummitNodes )
    ];

    public static IReadOnlyList<WorldMapSegmentDefinition> All { get; } =
        Array.AsReadOnly( SegmentData );

    public static WorldMapSegmentDefinition GetById( int id ) {
        if ( id is < 0 or >= 5 ) throw new ArgumentOutOfRangeException( nameof( id ) );
        return SegmentData[id];
    }

    public static WorldMapSegmentDefinition GetByLevel( int level ) {
        if ( level is < 1 or > WorldMapProgressSnapshot.MaximumLevel )
            throw new ArgumentOutOfRangeException( nameof( level ) );
        return SegmentData[(level - 1) / 20];
    }

    private static Vector4 Rgb( byte red, byte green, byte blue ) =>
        new( red / 255f, green / 255f, blue / 255f, 1f );
}

internal sealed class WorldMapSegmentVisibility {
    private readonly IReadOnlyList<WorldMapSegmentDefinition> _segments;
    private readonly bool[] _active;

    public float RetainMargin { get; }
    public int ActiveCount { get; private set; }
    public ulong Revision { get; private set; }

    public WorldMapSegmentVisibility(
        IReadOnlyList<WorldMapSegmentDefinition> segments,
        float retainMargin = 200f ) {
        ArgumentNullException.ThrowIfNull( segments );
        if ( segments.Count == 0 )
            throw new ArgumentException( "At least one WorldMap segment is required.", nameof( segments ) );
        if ( !float.IsFinite( retainMargin ) || retainMargin < 0f )
            throw new ArgumentOutOfRangeException( nameof( retainMargin ) );
        _segments = segments;
        _active = new bool[segments.Count];
        RetainMargin = retainMargin;
        for (int i = 0; i < segments.Count; i++) {
            if ( segments[i].Id != i )
                throw new ArgumentException(
                    "WorldMap segments must be ordered by contiguous IDs starting at zero.",
                    nameof( segments ) );
        }
    }

    public bool IsActive( int segmentId ) {
        if ( segmentId is < 0 || segmentId >= _active.Length )
            throw new ArgumentOutOfRangeException( nameof( segmentId ) );
        return _active[segmentId];
    }

    public bool Update( in Bounds2D visibleWorldBounds ) {
        var retained = new Bounds2D(
            visibleWorldBounds.Left,
            visibleWorldBounds.Top - RetainMargin,
            visibleWorldBounds.Right,
            visibleWorldBounds.Bottom + RetainMargin );
        bool changed = false;
        int activeCount = 0;
        for (int i = 0; i < _segments.Count; i++) {
            WorldMapSegmentDefinition segment = _segments[i];
            bool active = segment.Bounds.Intersects( retained );
            if ( active ) activeCount++;
            if ( _active[segment.Id] == active ) continue;
            _active[segment.Id] = active;
            changed = true;
        }
        ActiveCount = activeCount;
        if ( changed ) Revision++;
        return changed;
    }
}

internal sealed class WorldMapSegmentRuntimeGroup {
    private readonly GameInstance[] _members;

    public int SegmentId { get; }
    public bool IsActive { get; private set; } = true;
    public int MemberCount => _members.Length;

    public WorldMapSegmentRuntimeGroup( int segmentId, ReadOnlySpan<GameInstance> members ) {
        if ( segmentId is < 0 or > 4 ) throw new ArgumentOutOfRangeException( nameof( segmentId ) );
        if ( members.Length == 0 )
            throw new ArgumentException( "A runtime segment group cannot be empty.", nameof( members ) );
        for (int i = 0; i < members.Length; i++)
            ArgumentNullException.ThrowIfNull( members[i] );
        SegmentId = segmentId;
        _members = members.ToArray();
    }

    public void Apply( bool active, Action<IDomainEvent> raiseEvent ) {
        ArgumentNullException.ThrowIfNull( raiseEvent );
        if ( IsActive == active ) return;
        IsActive = active;
        for (int i = 0; i < _members.Length; i++)
            _members[i].SetActive( active, raiseEvent );
    }
}

internal sealed class WorldMapSegmentVisibilityController : GameInstance {
    private readonly Camera2D _camera;
    private readonly WorldMapSegmentVisibility _visibility;
    private readonly WorldMapSegmentRuntimeGroup[] _groups;
    private readonly Action<IDomainEvent> _raiseEvent;

    public int ActiveSegmentCount => _visibility.ActiveCount;
    public ulong VisibilityRevision => _visibility.Revision;

    public WorldMapSegmentVisibilityController(
        Camera2D camera,
        WorldMapSegmentVisibility visibility,
        ReadOnlySpan<WorldMapSegmentRuntimeGroup> groups,
        Action<IDomainEvent> raiseEvent ) {
        _camera = camera ?? throw new ArgumentNullException( nameof( camera ) );
        _visibility = visibility ?? throw new ArgumentNullException( nameof( visibility ) );
        if ( groups.Length == 0 )
            throw new ArgumentException( "At least one assembled segment group is required.", nameof( groups ) );
        _groups = groups.ToArray();
        _raiseEvent = raiseEvent ?? throw new ArgumentNullException( nameof( raiseEvent ) );
        TimeMode = InstanceTimeMode.Unscaled;
    }

    public override void OnStep( double deltaTime ) {
        if ( !_camera.TryGetStableVisibleWorldBounds( out Bounds2D visible ) ) return;
        UpdateVisibility( visible );
    }

    internal void UpdateVisibility( in Bounds2D visible ) {
        _visibility.Update( visible );
        for (int i = 0; i < _groups.Length; i++) {
            WorldMapSegmentRuntimeGroup group = _groups[i];
            group.Apply( _visibility.IsActive( group.SegmentId ), _raiseEvent );
        }
    }
}
