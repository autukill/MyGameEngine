namespace TheGodTheyMade.Simulation.World;

using TheGodTheyMade.Simulation.Familiar;

public enum MingzhongCommandJournalMode
{
    Disabled,
    Recording,
    Playback
}

public sealed class MingzhongCommandRecording
{
    public const int CurrentSchemaVersion = 1;
    private readonly MingzhongCommand[] _commands;

    public int SchemaVersion => CurrentSchemaVersion;
    public ReadOnlyMemory<MingzhongCommand> Commands => _commands;
    public int Count => _commands.Length;
    public long EndTick { get; }

    public MingzhongCommandRecording(long endTick, IEnumerable<MingzhongCommand> commands)
    {
        if (endTick < 0) throw new ArgumentOutOfRangeException(nameof(endTick));
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands.ToArray();
        Validate(_commands);
        if (_commands.Length > 0 && endTick < _commands[^1].Tick)
            throw new ArgumentException("End tick cannot precede the last command.", nameof(endTick));
        EndTick = endTick;
    }

    private static void Validate(ReadOnlySpan<MingzhongCommand> commands)
    {
        long previousTick = -1;
        for (int i = 0; i < commands.Length; i++)
        {
            ref readonly MingzhongCommand command = ref commands[i];
            if (command.Tick < 0 || command.Tick < previousTick)
                throw new ArgumentException("Commands must use non-negative, non-decreasing ticks.", nameof(commands));
            if (!Enum.IsDefined(command.Kind))
                throw new ArgumentException($"Unknown command kind '{command.Kind}'.", nameof(commands));
            if (command.Kind == MingzhongCommandKind.InvokeRain && command.RadiusCells is 0 or > 24)
                throw new ArgumentException("Rain radius must be within 1..24 cells.", nameof(commands));
            previousTick = command.Tick;
        }
    }
}

public sealed class MingzhongCommandJournal
{
    private readonly List<MingzhongCommand>? _recorded;
    private readonly MingzhongCommand[] _playback;
    private int _cursor;

    public MingzhongCommandJournalMode Mode { get; }
    public int Count => Mode == MingzhongCommandJournalMode.Recording
        ? _recorded!.Count
        : _playback.Length;
    public int PlaybackCursor => _cursor;
    public bool IsPlaybackComplete => Mode != MingzhongCommandJournalMode.Playback || _cursor == _playback.Length;
    public long PlaybackEndTick { get; }

    private MingzhongCommandJournal(
        MingzhongCommandJournalMode mode,
        List<MingzhongCommand>? recorded,
        MingzhongCommand[] playback,
        long playbackEndTick = 0)
    {
        Mode = mode;
        _recorded = recorded;
        _playback = playback;
        PlaybackEndTick = playbackEndTick;
    }

    public static MingzhongCommandJournal Disabled() =>
        new(MingzhongCommandJournalMode.Disabled, null, []);

    public static MingzhongCommandJournal Record(int initialCapacity = 16)
    {
        if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        return new MingzhongCommandJournal(
            MingzhongCommandJournalMode.Recording,
            new List<MingzhongCommand>(initialCapacity),
            []);
    }

    public static MingzhongCommandJournal Play(MingzhongCommandRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        return new MingzhongCommandJournal(
            MingzhongCommandJournalMode.Playback,
            null,
            recording.Commands.ToArray(),
            recording.EndTick);
    }

    public void RecordAccepted(in MingzhongCommand command)
    {
        if (Mode == MingzhongCommandJournalMode.Disabled) return;
        if (Mode != MingzhongCommandJournalMode.Recording)
            throw new InvalidOperationException("Cannot record into a playback command journal.");
        if (_recorded!.Count > 0 && command.Tick < _recorded[^1].Tick)
            throw new InvalidOperationException("Accepted commands must be recorded in tick order.");
        _recorded.Add(command);
    }

    public int ApplyCurrentTick(MingzhongWorldSimulation world, FamiliarLearning familiar)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(familiar);
        if (Mode != MingzhongCommandJournalMode.Playback) return 0;
        if (_cursor < _playback.Length && _playback[_cursor].Tick < world.Tick)
            throw new InvalidOperationException(
                $"Command playback missed tick {_playback[_cursor].Tick}; world is at {world.Tick}.");

        int applied = 0;
        while (_cursor < _playback.Length && _playback[_cursor].Tick == world.Tick)
        {
            MingzhongCommand command = _playback[_cursor++];
            bool accepted = command.Kind switch
            {
                MingzhongCommandKind.PraiseFamiliar => familiar.Reward(
                    FamiliarRewardReason.PlayerPraise,
                    FamiliarSituation.IdleVillage,
                    ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                    command.Tick),
                MingzhongCommandKind.StopFamiliar => familiar.Reward(
                    FamiliarRewardReason.PlayerStop,
                    FamiliarSituation.IdleVillage,
                    ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                    command.Tick),
                _ => world.TryApply(command)
            };
            if (!accepted)
                throw new InvalidOperationException(
                    $"Recorded command {command.Kind} was rejected at tick {command.Tick}.");
            applied++;
        }
        return applied;
    }

    public MingzhongCommandRecording Snapshot(long endTick)
    {
        if (Mode != MingzhongCommandJournalMode.Recording)
            throw new InvalidOperationException("Only a recording journal can create a snapshot.");
        return new MingzhongCommandRecording(endTick, _recorded!);
    }
}
