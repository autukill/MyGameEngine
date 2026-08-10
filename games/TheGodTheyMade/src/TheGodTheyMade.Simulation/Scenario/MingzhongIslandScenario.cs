namespace TheGodTheyMade.Simulation.Scenario;

using TheGodTheyMade.Simulation.Beliefs;
using TheGodTheyMade.Simulation.Familiar;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

public enum IslandChapterPhase
{
    Awakening,
    BeliefTrial,
    FuneralChoice,
    FinalAction,
    Completed
}

public enum GateResolution
{
    Unresolved,
    Familiar,
    Villagers
}

public enum RuinPuzzleState
{
    Dry,
    Revealed,
    Decoded
}

public enum FuneralOutcome
{
    Pending,
    Active,
    LanternsPreserved,
    LanternsLostToRain
}

public enum IslandEnding
{
    Flourished,
    Endured,
    Scarred
}

public readonly record struct MuralTriptych(
    string Awakening,
    string Guardian,
    string Cost);

public sealed class MingzhongIslandScenario
{
    public const int ChapterDurationTicks = 30 * 60 * MingzhongVillage.TicksPerSecond;
    public static readonly GridCell RuinTablet = new(42, 9);
    public static readonly GridCell FuneralGround = MingzhongVillage.Cemetery;

    private ulong _lastObservationId;
    private long _lastFamiliarActedTick = -1;
    private bool _funeralPublished;
    private bool _funeralFireExtinguished;

    public IslandChapterPhase Phase { get; private set; } = IslandChapterPhase.Awakening;
    public GateResolution GateResolution { get; private set; }
    public RuinPuzzleState Ruin { get; private set; }
    public FuneralOutcome Funeral { get; private set; }
    public IslandEnding? Ending { get; private set; }
    public MuralTriptych? Mural { get; private set; }
    public bool IsComplete => Phase == IslandChapterPhase.Completed;

    public void Advance(
        MingzhongWorldSimulation world,
        BeliefSimulation beliefs,
        FamiliarLearning familiar)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(beliefs);
        ArgumentNullException.ThrowIfNull(familiar);
        if (IsComplete) return;

        ProcessObservations(world);
        long tick = world.Tick;
        Phase = tick switch
        {
            < 10L * 60 * MingzhongVillage.TicksPerSecond => IslandChapterPhase.Awakening,
            < 18L * 60 * MingzhongVillage.TicksPerSecond => IslandChapterPhase.BeliefTrial,
            < 24L * 60 * MingzhongVillage.TicksPerSecond => IslandChapterPhase.FuneralChoice,
            < ChapterDurationTicks => IslandChapterPhase.FinalAction,
            _ => IslandChapterPhase.Completed
        };

        if (GateResolution == GateResolution.Unresolved &&
            tick >= 8L * 60 * MingzhongVillage.TicksPerSecond + 30 * MingzhongVillage.TicksPerSecond)
        {
            world.Publish(ObservationKind.FamiliarActed, "villagers.gate-team", null, MingzhongVillage.Gate);
            world.TryApply(MingzhongCommand.OpenGate(world.Tick));
            GateResolution = GateResolution.Villagers;
        }

        if (Ruin == RuinPuzzleState.Dry && world.IsCellCoveredByRain(RuinTablet))
            Ruin = RuinPuzzleState.Revealed;
        if (Ruin == RuinPuzzleState.Revealed &&
            tick >= 25L * 60 * MingzhongVillage.TicksPerSecond)
            Ruin = RuinPuzzleState.Decoded;

        long funeralStart = 18L * 60 * MingzhongVillage.TicksPerSecond;
        long funeralEnd = 24L * 60 * MingzhongVillage.TicksPerSecond;
        if (!_funeralPublished && tick >= funeralStart)
        {
            _funeralPublished = true;
            Funeral = FuneralOutcome.Active;
            world.Publish(ObservationKind.FuneralStarted, "funeral.mingzhong", null, FuneralGround);
            world.Publish(ObservationKind.FireStarted, "funeral.paper-lanterns", null, FuneralGround);
        }
        if (Funeral == FuneralOutcome.Active && world.IsCellCoveredByRain(FuneralGround))
        {
            Funeral = FuneralOutcome.LanternsLostToRain;
            if (!_funeralFireExtinguished)
            {
                _funeralFireExtinguished = true;
                world.Publish(ObservationKind.FireExtinguished, "funeral.paper-lanterns", null, FuneralGround);
            }
        }
        if (Funeral == FuneralOutcome.Active && tick >= funeralEnd)
            Funeral = FuneralOutcome.LanternsPreserved;

        if (Phase == IslandChapterPhase.Completed && Mural is null)
            Complete(world, beliefs, familiar);
    }

    public ulong ComputeStateHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        Add(ref hash, (ulong)Phase, prime);
        Add(ref hash, (ulong)GateResolution, prime);
        Add(ref hash, (ulong)Ruin, prime);
        Add(ref hash, (ulong)Funeral, prime);
        Add(ref hash, Ending is null ? ulong.MaxValue : (ulong)Ending.Value, prime);
        Add(ref hash, _lastObservationId, prime);
        Add(ref hash, unchecked((ulong)_lastFamiliarActedTick), prime);
        return hash;
    }

    private void ProcessObservations(MingzhongWorldSimulation world)
    {
        foreach (ref readonly WorldObservation observation in world.Observations)
        {
            if (observation.Id.Value <= _lastObservationId) continue;
            _lastObservationId = observation.Id.Value;
            if (observation.Kind == ObservationKind.FamiliarActed)
                _lastFamiliarActedTick = observation.Tick;
            if (observation.Kind == ObservationKind.GateOpened && GateResolution == GateResolution.Unresolved)
            {
                GateResolution = _lastFamiliarActedTick >= 0 &&
                                 observation.Tick - _lastFamiliarActedTick <= 8 * MingzhongVillage.TicksPerSecond
                    ? GateResolution.Familiar
                    : GateResolution.Villagers;
            }
        }
    }

    private void Complete(
        MingzhongWorldSimulation world,
        BeliefSimulation beliefs,
        FamiliarLearning familiar)
    {
        int withered = 0;
        for (int i = 0; i < world.FieldCount; i++)
            if (world.GetField(i).Withered) withered++;
        Ending = withered == 0 && GateResolution != GateResolution.Unresolved && Ruin == RuinPuzzleState.Decoded
            ? IslandEnding.Flourished
            : withered < 3 && GateResolution != GateResolution.Unresolved
                ? IslandEnding.Endured
                : IslandEnding.Scarred;

        string awakening = beliefs.Doctrine is { Key.Cause: ObservationKind.BellRang, Key.Effect: ObservationKind.RainStarted }
            ? "钟声唤醒了云上的神"
            : "枯萎的麦苗唤来了无言之雨";
        string guardian = GateResolution == GateResolution.Familiar
            ? "巨猿移开石门，让水重新找到道路"
            : GateResolution == GateResolution.Villagers
                ? "村民合力移开石门，守住了山谷"
                : "石门仍沉默，水井支撑村民留下";
        string cost = Funeral == FuneralOutcome.LanternsLostToRain
            ? "大雨救活土地，也带走了送行的灯"
            : Ruin == RuinPuzzleState.Dry
                ? "送行的灯得以燃尽，旧神的沟槽仍被遗忘"
                : withered > 0
                    ? "仪式与旧路留下，但有麦苗未能归来"
                    : "他们保存了灯火，也记住了水的旧路";
        _ = familiar;
        Mural = new MuralTriptych(awakening, guardian, cost);
    }

    private static void Add(ref ulong hash, ulong value, ulong prime)
    {
        hash ^= value;
        hash *= prime;
    }
}
