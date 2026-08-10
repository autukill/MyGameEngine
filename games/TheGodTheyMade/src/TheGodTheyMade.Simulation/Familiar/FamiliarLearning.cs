namespace TheGodTheyMade.Simulation.Familiar;

using GameEngine.Core.Domain.Gameplay;

public sealed class FamiliarLearning
{
    public const int Alpha = 350;
    public const int Gamma = 200;
    public const int Epsilon = 80;
    public const int DemonstrationPrior = 800;
    public const int DecisionCooldownTicks = 60;
    public const int FailureCooldownTicks = 180;
    public const int CreditWindowTicks = 5 * 60;
    public const int MinimumQ = -8000;
    public const int MaximumQ = 8000;
    public const int TraceCapacity = 16;
    private const int SituationCount = 7;
    private const int ActionCount = 6;

    private readonly int[] _q = new int[SituationCount * ActionCount];
    private readonly long[] _blockedUntil = new long[ActionCount];
    private readonly FamiliarDecisionTrace[] _traces = new FamiliarDecisionTrace[TraceCapacity];
    private readonly GameplayRandom _random;
    private readonly FamiliarTemperament _temperament;
    private int _traceStart;
    private int _traceCount;
    private long _nextDecisionTick;
    private FamiliarDecision? _lastDecision;

    public int TrustPermille { get; private set; } = 1000;
    public int TraceCount => _traceCount;
    public FamiliarDecision? LastDecision => _lastDecision;

    public FamiliarLearning(ulong seed, FamiliarTemperament? temperament = null)
    {
        _random = new GameplayRandom(seed);
        _temperament = (temperament ?? FamiliarTemperament.Default).Validate();
    }

    public int GetQ(FamiliarSituation situation, FamiliarAction action) => _q[Index(situation, action)];

    public FamiliarDecisionTrace GetTrace(int index)
    {
        if ((uint)index >= (uint)_traceCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _traces[(_traceStart + index) % TraceCapacity];
    }

    public bool TryChoose(
        FamiliarSituation situation,
        FamiliarActionMask authoredLegalActions,
        long tick,
        out FamiliarDecision decision)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (tick < _nextDecisionTick)
        {
            decision = default;
            return false;
        }

        FamiliarActionMask legal = FilterCooldowns(authoredLegalActions, tick);
        int legalCount = Count(legal);
        if (legalCount == 0)
        {
            decision = default;
            return false;
        }

        bool explored = legalCount > 1 && _random.NextInt(1000) < Epsilon;
        FamiliarAction selected;
        int score;
        if (explored)
        {
            int choice = _random.NextInt(legalCount);
            selected = Nth(legal, choice);
            score = Score(situation, selected);
        }
        else
        {
            selected = First(legal);
            score = Score(situation, selected);
            for (int actionIndex = (int)selected + 1; actionIndex < ActionCount; actionIndex++)
            {
                FamiliarAction candidate = (FamiliarAction)actionIndex;
                if (!ApeFamiliarBody.Contains(legal, candidate)) continue;
                int candidateScore = Score(situation, candidate);
                if (candidateScore > score)
                {
                    selected = candidate;
                    score = candidateScore;
                }
            }
        }

        decision = new FamiliarDecision(tick, situation, selected, score, explored);
        _lastDecision = decision;
        _nextDecisionTick = tick + DecisionCooldownTicks;
        AppendTrace(new FamiliarDecisionTrace(
            tick, situation, selected, FamiliarRewardReason.None,
            0, GetQ(situation, selected), GetQ(situation, selected), explored));
        return true;
    }

    public void Demonstrate(FamiliarSituation situation, FamiliarAction action, long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        int index = Index(situation, action);
        int previous = _q[index];
        _q[index] = Math.Clamp(previous + DemonstrationPrior, MinimumQ, MaximumQ);
        AppendTrace(new FamiliarDecisionTrace(
            tick, situation, action, FamiliarRewardReason.SafeDiscovery,
            DemonstrationPrior, previous, _q[index], false));
    }

    public bool Reward(
        FamiliarRewardReason reason,
        FamiliarSituation nextSituation,
        FamiliarActionMask nextLegalActions,
        long tick)
    {
        if (reason == FamiliarRewardReason.None) throw new ArgumentOutOfRangeException(nameof(reason));
        if (_lastDecision is not { } decision || tick < decision.Tick ||
            tick - decision.Tick > CreditWindowTicks) return false;

        int reward = RewardValue(reason);
        int maxNext = MaxQ(nextSituation, nextLegalActions);
        int index = Index(decision.Situation, decision.Action);
        int previous = _q[index];
        int target = reward + Gamma * maxNext / 1000;
        int delta = Alpha * (target - previous) / 1000;
        _q[index] = Math.Clamp(previous + delta, MinimumQ, MaximumQ);
        if (reason == FamiliarRewardReason.AffordanceFailed)
            _blockedUntil[(int)decision.Action] = Math.Max(
                _blockedUntil[(int)decision.Action], tick + FailureCooldownTicks);
        AppendTrace(new FamiliarDecisionTrace(
            tick, decision.Situation, decision.Action, reason,
            reward, previous, _q[index], decision.Explored));
        return true;
    }

    public void SetTrustPermille(int value) =>
        TrustPermille = Math.Clamp(value, 750, 1250);

    public FamiliarLearningSnapshot CaptureSnapshot()
    {
        var traces = new FamiliarDecisionTrace[_traceCount];
        for (int i = 0; i < traces.Length; i++) traces[i] = GetTrace(i);
        return new FamiliarLearningSnapshot(
            (int[])_q.Clone(),
            (long[])_blockedUntil.Clone(),
            traces,
            _random.CaptureState(),
            _nextDecisionTick,
            _lastDecision,
            TrustPermille);
    }

    public void RestoreSnapshot(FamiliarLearningSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.QValues.Length != _q.Length || snapshot.BlockedUntilTicks.Length != _blockedUntil.Length ||
            snapshot.Traces.Length > TraceCapacity || snapshot.TrustPermille is < 750 or > 1250 ||
            snapshot.QValues.Any(value => value is < MinimumQ or > MaximumQ))
            throw new ArgumentException("Familiar learning snapshot is incompatible or out of range.", nameof(snapshot));
        snapshot.QValues.CopyTo(_q, 0);
        snapshot.BlockedUntilTicks.CopyTo(_blockedUntil, 0);
        Array.Clear(_traces);
        snapshot.Traces.CopyTo(_traces, 0);
        _traceStart = 0;
        _traceCount = snapshot.Traces.Length;
        _random.RestoreState(snapshot.RandomState);
        _nextDecisionTick = snapshot.NextDecisionTick;
        _lastDecision = snapshot.LastDecision;
        TrustPermille = snapshot.TrustPermille;
    }

    public ulong ComputeStateHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        Add(ref hash, _random.CaptureState().Value, prime);
        Add(ref hash, unchecked((ulong)_nextDecisionTick), prime);
        Add(ref hash, (ulong)TrustPermille, prime);
        for (int i = 0; i < _q.Length; i++) Add(ref hash, unchecked((ulong)_q[i]), prime);
        for (int i = 0; i < _blockedUntil.Length; i++) Add(ref hash, unchecked((ulong)_blockedUntil[i]), prime);
        Add(ref hash, (ulong)_traceCount, prime);
        for (int i = 0; i < _traceCount; i++)
        {
            FamiliarDecisionTrace trace = GetTrace(i);
            Add(ref hash, unchecked((ulong)trace.Tick), prime);
            Add(ref hash, (ulong)trace.Situation, prime);
            Add(ref hash, (ulong)trace.Action, prime);
            Add(ref hash, (ulong)trace.Reason, prime);
            Add(ref hash, unchecked((ulong)trace.NewQ), prime);
        }
        return hash;
    }

    private int RewardValue(FamiliarRewardReason reason) => reason switch
    {
        FamiliarRewardReason.PlayerPraise => 2500 * TrustPermille / 1000,
        FamiliarRewardReason.PlayerStop => -3000 * TrustPermille / 1000,
        FamiliarRewardReason.CropRecovered => 1600,
        FamiliarRewardReason.FireExtinguished => 2200,
        FamiliarRewardReason.GateOpened => 1800,
        FamiliarRewardReason.VillagerRescued => 1400,
        FamiliarRewardReason.VillagerInjured => -4000,
        FamiliarRewardReason.NoEffect => -300,
        FamiliarRewardReason.AffordanceFailed => -600,
        FamiliarRewardReason.SafeDiscovery => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    private int MaxQ(FamiliarSituation situation, FamiliarActionMask legal)
    {
        int max = 0;
        bool found = false;
        for (int i = 0; i < ActionCount; i++)
        {
            FamiliarAction action = (FamiliarAction)i;
            if (!ApeFamiliarBody.Contains(legal, action)) continue;
            int value = GetQ(situation, action);
            if (!found || value > max) { max = value; found = true; }
        }
        return found ? max : 0;
    }

    private int Score(FamiliarSituation situation, FamiliarAction action) =>
        GetQ(situation, action) + Instinct(situation, action) + Personality(action) - Risk(situation, action);

    private static int Instinct(FamiliarSituation situation, FamiliarAction action) => (situation, action) switch
    {
        (FamiliarSituation.FireEmergency, FamiliarAction.FetchWater) => 300,
        (FamiliarSituation.VillagerInDanger, FamiliarAction.ComfortVillager) => 280,
        (FamiliarSituation.BlockedWaterGate, FamiliarAction.CarryObject) => 240,
        (FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater) => 260,
        (FamiliarSituation.DryCropNeedsWater, FamiliarAction.FetchWater) => 220,
        (FamiliarSituation.BellGathering, FamiliarAction.RingBell) => 100,
        (_, FamiliarAction.Flee) => 20,
        _ => 0
    };

    private int Personality(FamiliarAction action) => action switch
    {
        FamiliarAction.FetchWater or FamiliarAction.CarryObject or FamiliarAction.RingBell => _temperament.Curiosity,
        FamiliarAction.PourWater or FamiliarAction.ComfortVillager => _temperament.Empathy,
        FamiliarAction.Flee => _temperament.Caution,
        _ => _temperament.Autonomy
    };

    private static int Risk(FamiliarSituation situation, FamiliarAction action) =>
        situation == FamiliarSituation.FireEmergency && action != FamiliarAction.Flee ? 80 : 0;

    private FamiliarActionMask FilterCooldowns(FamiliarActionMask actions, long tick)
    {
        for (int i = 0; i < ActionCount; i++)
            if (tick < _blockedUntil[i]) actions &= ~(FamiliarActionMask)(1 << i);
        return actions;
    }

    private void AppendTrace(in FamiliarDecisionTrace trace)
    {
        if (_traceCount < TraceCapacity)
        {
            _traces[(_traceStart + _traceCount++) % TraceCapacity] = trace;
            return;
        }
        _traces[_traceStart] = trace;
        _traceStart = (_traceStart + 1) % TraceCapacity;
    }

    private static int Index(FamiliarSituation situation, FamiliarAction action)
    {
        if ((uint)situation >= SituationCount) throw new ArgumentOutOfRangeException(nameof(situation));
        if ((uint)action >= ActionCount) throw new ArgumentOutOfRangeException(nameof(action));
        return (int)situation * ActionCount + (int)action;
    }

    private static int Count(FamiliarActionMask mask) => System.Numerics.BitOperations.PopCount((uint)mask);

    private static FamiliarAction First(FamiliarActionMask mask) => Nth(mask, 0);

    private static FamiliarAction Nth(FamiliarActionMask mask, int index)
    {
        for (int i = 0; i < ActionCount; i++)
        {
            FamiliarAction action = (FamiliarAction)i;
            if (!ApeFamiliarBody.Contains(mask, action)) continue;
            if (index-- == 0) return action;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private static void Add(ref ulong hash, ulong value, ulong prime)
    {
        hash ^= value;
        hash *= prime;
    }
}

public sealed record FamiliarLearningSnapshot(
    int[] QValues,
    long[] BlockedUntilTicks,
    FamiliarDecisionTrace[] Traces,
    GameplayRandomState RandomState,
    long NextDecisionTick,
    FamiliarDecision? LastDecision,
    int TrustPermille);
