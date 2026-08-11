namespace BubbleTa.Game.Home;

using GameEngine.Core.Domain.ValueObjects;

internal enum LogoRevealKind : byte
{
    Scale,
    Alpha,
    DropScale
}

internal readonly record struct LogoPlacement(
    Vector2D Position,
    int Depth,
    double DelaySeconds,
    LogoRevealKind Reveal);

internal readonly record struct SpritePlacement(
    Vector2D Position,
    Vector2D Scale,
    int Depth);

internal static class HomeSceneLayout
{
    public const double LegacyFramesPerSecond = 46d;
    public const ulong RandomSeed = 0xBABB1E7A2015UL;
    public static readonly Vector2D CameraPosition = new(120f, 0f);
    public static readonly Vector2D BackgroundPosition = Vector2D.Zero;
    public static readonly Vector2D BubblePosition = new(492f, 787f);
    public static readonly Vector2D CloudPosition = new(100f, 1210f);
    public static readonly Vector2D HeroPosition = new(480f, 640f);
    public static readonly Vector2D SnowPosition = new(672f, 768f);
    public static readonly Vector2D KingPosition = new(416f, 832f);
    public static readonly Vector2D WorldButtonPosition = new(512f, 1056f);
    public static readonly Vector2D SettingsPosition = new(184f, 1206f);

    private static readonly LogoPlacement[] LogoItems =
    [
        new(new Vector2D(337f, 192f), -100, Frames(10), LogoRevealKind.Scale),
        new(new Vector2D(426f, 191f), -100, Frames(18), LogoRevealKind.Scale),
        new(new Vector2D(518f, 190f), -100, Frames(23), LogoRevealKind.Scale),
        new(new Vector2D(606f, 190f), -100, Frames(28), LogoRevealKind.Scale),
        new(new Vector2D(677f, 210f), -100, Frames(33), LogoRevealKind.Scale),
        new(new Vector2D(724f, 209f), -100, Frames(38), LogoRevealKind.Scale),
        new(new Vector2D(523f, 190f), -99, Frames(35), LogoRevealKind.Alpha),
        new(new Vector2D(323f, 125f), -98, Frames(45), LogoRevealKind.DropScale),
        new(new Vector2D(516f, 127f), -98, Frames(45), LogoRevealKind.DropScale),
        new(new Vector2D(619f, 144f), -95, Frames(45), LogoRevealKind.DropScale),
        new(new Vector2D(524f, 59f), -98, Frames(45), LogoRevealKind.DropScale),
        new(new Vector2D(412f, 107f), -97, Frames(50), LogoRevealKind.DropScale)
    ];

    private static readonly SpritePlacement[] StarItems =
    [
        new(new Vector2D(768f, 128f), Vector2D.One, -9),
        new(new Vector2D(607f, 280f), new Vector2D(.5f, .5f), -9),
        new(new Vector2D(95f, 588f), new Vector2D(.8f, .8f), -9)
    ];

    private static readonly Vector2D[] SpotItems =
    [
        new(32f, 384f),
        new(224f, 128f),
        new(864f, 512f),
        new(928f, 736f),
        new(32f, 832f)
    ];

    private static readonly Vector2D[] MeteorItems =
    [
        new(960f, -64f),
        new(832f, -160f),
        new(1088f, 32f),
        new(1184f, 128f),
        new(1184f, 288f)
    ];

    public static ReadOnlySpan<LogoPlacement> Logos => LogoItems;
    public static ReadOnlySpan<SpritePlacement> Stars => StarItems;
    public static ReadOnlySpan<Vector2D> Spots => SpotItems;
    public static ReadOnlySpan<Vector2D> Meteors => MeteorItems;

    public static ulong SeedFor(int index) =>
        unchecked(RandomSeed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);

    private static double Frames(int count) => count / LegacyFramesPerSecond;
}

internal static class HomeAnimationMath
{
    public static double PingPong(double elapsed, double oneWayDuration)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (!double.IsFinite(oneWayDuration) || oneWayDuration <= 0d)
            throw new ArgumentOutOfRangeException(nameof(oneWayDuration));
        double phase = elapsed % (oneWayDuration * 2d) / oneWayDuration;
        return phase <= 1d ? phase : 2d - phase;
    }
}
