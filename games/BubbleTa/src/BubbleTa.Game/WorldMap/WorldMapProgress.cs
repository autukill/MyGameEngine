namespace BubbleTa.Game.WorldMap;

internal enum WorldMapLevelState {
    Locked,
    Available,
    Completed
}

internal readonly record struct WorldMapLevelSelectionRequested(
    int Level,
    WorldMapNodeKind Kind,
    WorldMapLevelState State );

internal sealed class WorldMapProgressSnapshot {
    public const int MaximumLevel = 100;
    private readonly byte[] _stars;

    public static WorldMapProgressSnapshot NewGame { get; } = new( 1, [] );

    public int HighestUnlockedLevel { get; }

    public WorldMapProgressSnapshot(
        int highestUnlockedLevel,
        ReadOnlySpan<byte> stars ) {
        if ( highestUnlockedLevel is < 1 or > MaximumLevel )
            throw new ArgumentOutOfRangeException( nameof( highestUnlockedLevel ) );
        if ( stars.Length > MaximumLevel )
            throw new ArgumentException(
                $"A WorldMap progress snapshot supports at most {MaximumLevel} star entries.",
                nameof( stars ) );

        for (int i = 0; i < stars.Length; i++) {
            if ( stars[i] > 3 )
                throw new ArgumentOutOfRangeException(
                    nameof( stars ),
                    $"Level {i + 1} has {stars[i]} stars; the valid range is 0 through 3." );
            if ( i + 1 >= highestUnlockedLevel && stars[i] != 0 )
                throw new ArgumentException(
                    "Locked and currently available levels cannot contain completion stars.",
                    nameof( stars ) );
        }

        HighestUnlockedLevel = highestUnlockedLevel;
        _stars = stars.ToArray();
    }

    public WorldMapLevelState GetState( int level ) {
        ValidateLevel( level );
        if ( level > HighestUnlockedLevel ) return WorldMapLevelState.Locked;
        return level == HighestUnlockedLevel
            ? WorldMapLevelState.Available
            : WorldMapLevelState.Completed;
    }

    public int GetStars( int level ) {
        ValidateLevel( level );
        return level <= _stars.Length ? _stars[level - 1] : 0;
    }

    private static void ValidateLevel( int level ) {
        if ( level is < 1 or > MaximumLevel )
            throw new ArgumentOutOfRangeException( nameof( level ) );
    }
}
