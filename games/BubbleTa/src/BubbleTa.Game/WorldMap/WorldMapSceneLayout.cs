namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ViewportNavigation;

internal enum WorldMapNodeKind {
    Normal,
    Timed,
    Moving
}

internal readonly record struct WorldMapNodePlacement(
    int Level,
    Vector2D Position,
    WorldMapNodeKind Kind );

internal readonly record struct WorldMapCloudPlacement(
    Vector2D Position,
    float Scale,
    float SubImage,
    float Phase,
    int Depth );

internal readonly record struct WorldMapPersonPlacement(
    Vector2D Position,
    Vector2D JumpOffset,
    double InitialDelaySeconds,
    float RotationRadians );

internal readonly record struct WorldMapApplePlacement(
    Vector2D Position,
    float EndY,
    double InitialDelaySeconds,
    ulong Seed );

internal static class WorldMapSceneLayout {
    public const float FirstIslandY = 13_300f;
    public const float FirstIslandSeamY = 14_636f;
    public const float FirstIslandBottomY = 15_972f;
    public const float ViewWidth = 720f;
    public const int BirdDepth = -5;

    public static Bounds2D RoomBounds { get; } = new( 0f, 0f, 1_048f, 16_100f );
    public static Vector2 InitialCameraPosition { get; } = new( 164f, 14_820f );
    public static Bounds2D NavigationBounds { get; } = new(
        InitialCameraPosition.X,
        RoomBounds.Top,
        InitialCameraPosition.X + ViewWidth,
        RoomBounds.Bottom );
    public static ViewportDragOptions NavigationDrag { get; } =
        new(
            ViewportAxis.All,
            8f,
            ViewportDragAxisLock.Dominant,
            dominanceRatio: 1.25f );
    public static ViewportBounceOptions NavigationBounce { get; } =
        new( NavigationBounds, ViewportAxis.All );
    public static Vector2D IslandUpperPosition { get; } = new( 538f, FirstIslandY );
    public static Vector2D IslandLowerPosition { get; } = new( 538f, FirstIslandBottomY );
    public static Vector2D SmokePosition { get; } = new( 220f, 15_360f );
    public static Vector2D StonePosition { get; } = new( 558f, 14_936f );
    public static Vector2D MushroomPosition { get; } = new( 560f, 15_625f );
    public static Vector2D BirdPosition { get; } = new( 0f, 14_729f );
    public static Vector2D LuteaPosition { get; } = new( 466f, 14_485f );

    private static readonly WorldMapPersonPlacement[] PersonData = [
        new( new Vector2D( 508f, 14_936f ), new Vector2D( -60f, -22f ), 1d / 46d, MathF.PI / 6f ),
        new( new Vector2D( 582f, 14_956f ), new Vector2D( 0f, -90f ), 4d, 0f ),
        new( new Vector2D( 558f, 14_906f ), new Vector2D( -60f, -30f ), 2d, MathF.PI / 6f )
    ];

    private static readonly WorldMapApplePlacement[] AppleData = [
        new( new Vector2D( 620f, 13_450f ), 13_690f, 2d, 0xA11E_0001UL ),
        new( new Vector2D( 719f, 13_460f ), 13_690f, 5d, 0xA11E_0002UL ),
        new( new Vector2D( 790f, 13_407f ), 13_670f, 7d, 0xA11E_0003UL )
    ];

    private static readonly WorldMapNodePlacement[] FirstIslandNodeData = [
        new( 1, new Vector2D( 511f, 15_545f ), WorldMapNodeKind.Normal ),
        new( 2, new Vector2D( 650f, 15_513f ), WorldMapNodeKind.Normal ),
        new( 3, new Vector2D( 758f, 15_427f ), WorldMapNodeKind.Normal ),
        new( 4, new Vector2D( 739f, 15_306f ), WorldMapNodeKind.Timed ),
        new( 5, new Vector2D( 630f, 15_225f ), WorldMapNodeKind.Normal ),
        new( 6, new Vector2D( 491f, 15_161f ), WorldMapNodeKind.Normal ),
        new( 7, new Vector2D( 381f, 15_075f ), WorldMapNodeKind.Normal ),
        new( 8, new Vector2D( 332f, 14_962f ), WorldMapNodeKind.Timed ),
        new( 9, new Vector2D( 391f, 14_841f ), WorldMapNodeKind.Moving ),
        new( 10, new Vector2D( 531f, 14_784f ), WorldMapNodeKind.Normal ),
        new( 11, new Vector2D( 657f, 14_729f ), WorldMapNodeKind.Timed ),
        new( 12, new Vector2D( 739f, 14_650f ), WorldMapNodeKind.Normal ),
        new( 13, new Vector2D( 716f, 14_511f ), WorldMapNodeKind.Moving ),
        new( 14, new Vector2D( 616f, 14_383f ), WorldMapNodeKind.Normal ),
        new( 15, new Vector2D( 487f, 14_291f ), WorldMapNodeKind.Timed ),
        new( 16, new Vector2D( 396f, 14_191f ), WorldMapNodeKind.Normal ),
        new( 17, new Vector2D( 435f, 14_026f ), WorldMapNodeKind.Moving ),
        new( 18, new Vector2D( 597f, 13_893f ), WorldMapNodeKind.Normal ),
        new( 19, new Vector2D( 533f, 13_734f ), WorldMapNodeKind.Normal ),
        new( 20, new Vector2D( 460f, 13_628f ), WorldMapNodeKind.Normal )
    ];

    private static readonly WorldMapCloudPlacement[] UnderCloudData = [
        new( new Vector2D( -80f, 13_160f ), 1.05f, 0f, .1f, 200 ),
        new( new Vector2D( 805f, 13_340f ), .95f, 1f, .8f, 200 ),
        new( new Vector2D( -95f, 13_760f ), 1.1f, 2f, 1.6f, 200 ),
        new( new Vector2D( 800f, 14_070f ), .9f, 3f, 2.2f, 200 ),
        new( new Vector2D( -110f, 14_520f ), 1f, 1f, 2.9f, 200 ),
        new( new Vector2D( 790f, 14_940f ), 1.05f, 2f, 3.5f, 200 ),
        new( new Vector2D( -75f, 15_360f ), .95f, 3f, 4.1f, 200 ),
        new( new Vector2D( 785f, 15_710f ), 1.1f, 0f, 5f, 200 )
    ];

    private static readonly WorldMapCloudPlacement[] AboveCloudData = [
        new( new Vector2D( -105f, 13_410f ), 1.15f, 2f, .5f, -100 ),
        new( new Vector2D( 815f, 13_700f ), 1f, 0f, 1.2f, -100 ),
        new( new Vector2D( -125f, 14_220f ), 1.05f, 1f, 2f, -100 ),
        new( new Vector2D( 790f, 14_620f ), 1.15f, 3f, 2.8f, -100 ),
        new( new Vector2D( -90f, 15_060f ), 1.1f, 0f, 3.7f, -100 ),
        new( new Vector2D( 810f, 15_480f ), 1.05f, 2f, 4.6f, -100 )
    ];

    public static ReadOnlySpan<WorldMapNodePlacement> FirstIslandNodes => FirstIslandNodeData;
    public static ReadOnlySpan<WorldMapCloudPlacement> UnderClouds => UnderCloudData;
    public static ReadOnlySpan<WorldMapCloudPlacement> AboveClouds => AboveCloudData;
    public static ReadOnlySpan<WorldMapPersonPlacement> People => PersonData;
    public static ReadOnlySpan<WorldMapApplePlacement> Apples => AppleData;
}
