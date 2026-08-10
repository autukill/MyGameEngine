namespace TheGodTheyMade.Simulation.Beliefs;

using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

public sealed class BeliefSimulation
{
    public const int MaxHypothesesPerVillager = 8;
    public const int MaxPendingCausesPerVillager = 16;
    private static readonly CausalRule[] Rules =
    [
        Rule(ObservationKind.BellRang, ObservationKind.RainStarted, 8),
        Rule(ObservationKind.OfferingPlaced, ObservationKind.RainStarted, 8),
        Rule(ObservationKind.FamiliarArrived, ObservationKind.RainStarted, 8),
        Rule(ObservationKind.CropWithered, ObservationKind.RainStarted, 12),
        Rule(ObservationKind.RainStarted, ObservationKind.CropRecovered, 15),
        Rule(ObservationKind.FamiliarActed, ObservationKind.GateOpened, 8),
        Rule(ObservationKind.FuneralStarted, ObservationKind.RainStarted, 12),
        Rule(ObservationKind.BellRang, ObservationKind.CropRecovered, 20)
    ];

    private readonly Mind[] _minds;
    private long _lastGatheringTick = -1;

    public PublicDoctrine? Doctrine { get; private set; }
    public int VillagerCount => _minds.Length;

    public BeliefSimulation(IReadOnlyList<VillagerDefinition> villagers)
    {
        ArgumentNullException.ThrowIfNull(villagers);
        _minds = new Mind[villagers.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < villagers.Count; i++)
        {
            VillagerDefinition definition = villagers[i];
            if (!ids.Add(definition.Id.Value))
                throw new ArgumentException($"Duplicate villager ID '{definition.Id}'.", nameof(villagers));
            _minds[i] = new Mind(definition);
            if (definition.Id.Value is "cen_bellkeeper" or "mian_ritualist")
            {
                _minds[i].ApplyPrior(
                    new BeliefHypothesisKey(ObservationKind.BellRang, ObservationKind.RainStarted),
                    120);
            }
        }
    }

    public void Update(MingzhongWorldSimulation world)
    {
        ArgumentNullException.ThrowIfNull(world);
        for (int i = 0; i < _minds.Length; i++)
        {
            Mind mind = _minds[i];
            VillagerObservationMemory memory = world.GetMemory(mind.Definition.Id);
            foreach (ref readonly WorldObservation observation in memory.Items)
            {
                if (observation.Id.Value <= mind.LastProcessedEventId) continue;
                ProcessObservation(mind, observation);
                mind.LastProcessedEventId = observation.Id.Value;
            }
            ExpirePending(mind, world.Tick);
        }

        if (world.Tick == 300L * MingzhongVillage.TicksPerSecond ||
            world.Tick == 510L * MingzhongVillage.TicksPerSecond)
        {
            ConductGathering(world.Tick);
        }
    }

    public BeliefHypothesisSnapshot? GetHypothesis(
        VillagerId villager,
        BeliefHypothesisKey key)
    {
        Mind mind = GetMind(villager);
        int index = mind.FindHypothesis(key);
        return index < 0 ? null : mind.Hypotheses[index].Snapshot;
    }

    public int GetHypothesisCount(VillagerId villager) => GetMind(villager).HypothesisCount;

    public VillageBeliefBehavior GetBehavior(VillagerId villager)
    {
        Mind mind = GetMind(villager);
        var bellRain = new BeliefHypothesisKey(
            ObservationKind.BellRang,
            ObservationKind.RainStarted);
        int index = mind.FindHypothesis(bellRain);
        int score = index < 0 ? 0 : mind.Hypotheses[index].Score;
        return new VillageBeliefBehavior(
            PrioritizeBell: score >= 300,
            MaintainBell: score >= 100,
            AttendDoctrineGathering: Doctrine is not null || score >= 100);
    }

    public void ConductGathering(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (_lastGatheringTick == tick) return;
        _lastGatheringTick = tick;

        for (int ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
        {
            BeliefHypothesisKey key = Rules[ruleIndex].Key;
            Mind? advocate = null;
            for (int i = 0; i < _minds.Length; i++)
            {
                Mind candidate = _minds[i];
                int hypothesisIndex = candidate.FindHypothesis(key);
                if (hypothesisIndex < 0 || candidate.Hypotheses[hypothesisIndex].Score < 450) continue;
                if (advocate is null ||
                    candidate.Definition.SocialInfluence > advocate.Definition.SocialInfluence ||
                    candidate.Definition.SocialInfluence == advocate.Definition.SocialInfluence &&
                    string.CompareOrdinal(candidate.Definition.Id.Value, advocate.Definition.Id.Value) < 0)
                {
                    advocate = candidate;
                }
            }
            if (advocate is null) continue;

            int responders = 0;
            for (int i = 0; i < _minds.Length; i++)
            {
                Mind listener = _minds[i];
                if (ReferenceEquals(listener, advocate)) continue;
                int testimony = Math.Min(80,
                    advocate.Definition.SocialInfluence * listener.Definition.ObservationReliability / 100);
                listener.ApplyTestimony(key, testimony, tick);
                int listenerIndex = listener.FindHypothesis(key);
                if (listenerIndex >= 0 && listener.Hypotheses[listenerIndex].Score >= 300)
                    responders++;
            }

            if (responders >= 2 && Doctrine is null)
                Doctrine = new PublicDoctrine(key, advocate.Definition.Id.Value, responders, tick);
        }
    }

    public ulong ComputeStateHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        Add(ref hash, unchecked((ulong)_lastGatheringTick), prime);
        Add(ref hash, Doctrine is null ? 0UL : 1UL, prime);
        if (Doctrine is { } doctrine)
        {
            Add(ref hash, (ulong)doctrine.Key.Cause, prime);
            Add(ref hash, (ulong)doctrine.Key.Effect, prime);
            AddString(ref hash, doctrine.AdvocateId, prime);
            Add(ref hash, (ulong)doctrine.Responders, prime);
        }
        for (int i = 0; i < _minds.Length; i++)
        {
            Mind mind = _minds[i];
            AddString(ref hash, mind.Definition.Id.Value, prime);
            Add(ref hash, (ulong)mind.HypothesisCount, prime);
            for (int j = 0; j < mind.HypothesisCount; j++)
            {
                ref Hypothesis hypothesis = ref mind.Hypotheses[j];
                Add(ref hash, (ulong)hypothesis.Key.Cause, prime);
                Add(ref hash, (ulong)hypothesis.Key.Effect, prime);
                Add(ref hash, unchecked((ulong)hypothesis.Score), prime);
                Add(ref hash, hypothesis.SupportingEvidence, prime);
                Add(ref hash, hypothesis.Contradictions, prime);
            }
        }
        return hash;
    }

    private static void ProcessObservation(Mind mind, in WorldObservation observation)
    {
        for (int i = 0; i < mind.PendingCount; i++)
        {
            ref PendingCause pending = ref mind.Pending[i];
            CausalRule rule = Rules[pending.RuleIndex];
            if (pending.Resolved || rule.Key.Effect != observation.Kind ||
                observation.Tick > pending.DeadlineTick) continue;
            int temporal = Math.Max(1,
                100 - (int)((observation.Tick - pending.Cause.Tick) * 100 / rule.WindowTicks));
            int spatial = SpatialFactor(pending.Cause.Cell, observation.Cell);
            int evidence = Evidence(
                Math.Min(pending.Cause.Salience, observation.Salience),
                temporal,
                spatial,
                mind.Definition.ObservationReliability);
            mind.ApplySupport(rule.Key, evidence, pending.Cause.Id, observation.Id, observation.Tick);
            pending.Resolved = true;
        }

        for (int ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
        {
            if (Rules[ruleIndex].Key.Cause != observation.Kind) continue;
            mind.AddPending(ruleIndex, observation);
        }
    }

    private static void ExpirePending(Mind mind, long tick)
    {
        int write = 0;
        for (int read = 0; read < mind.PendingCount; read++)
        {
            PendingCause pending = mind.Pending[read];
            if (tick <= pending.DeadlineTick)
            {
                mind.Pending[write++] = pending;
                continue;
            }
            if (!pending.Resolved)
            {
                CausalRule rule = Rules[pending.RuleIndex];
                int evidence = Evidence(
                    pending.Cause.Salience,
                    100,
                    100,
                    mind.Definition.ObservationReliability);
                mind.ApplyContradiction(rule.Key, evidence, pending.Cause.Id, tick);
            }
        }
        mind.PendingCount = write;
    }

    private Mind GetMind(VillagerId villager)
    {
        for (int i = 0; i < _minds.Length; i++)
            if (_minds[i].Definition.Id == villager) return _minds[i];
        throw new KeyNotFoundException($"Unknown villager '{villager}'.");
    }

    private static int Evidence(int salience, int temporal, int distance, int reliability) =>
        Math.Clamp(salience * temporal * distance * reliability / 1_000_000, 1, 100);

    private static int SpatialFactor(in Navigation.GridCell cause, in Navigation.GridCell effect)
    {
        int distance = Math.Abs(cause.X - effect.X) + Math.Abs(cause.Y - effect.Y);
        return Math.Max(20, 100 - distance * 5);
    }

    private static CausalRule Rule(ObservationKind cause, ObservationKind effect, int seconds) =>
        new(new BeliefHypothesisKey(cause, effect), seconds * MingzhongVillage.TicksPerSecond);

    private static void Add(ref ulong hash, ulong value, ulong prime)
    {
        hash ^= value;
        hash *= prime;
    }

    private static void AddString(ref ulong hash, string value, ulong prime)
    {
        for (int i = 0; i < value.Length; i++) Add(ref hash, value[i], prime);
    }

    private readonly record struct CausalRule(BeliefHypothesisKey Key, int WindowTicks);

    private struct PendingCause
    {
        public int RuleIndex;
        public WorldObservation Cause;
        public long DeadlineTick;
        public bool Resolved;
    }

    private struct Hypothesis
    {
        public BeliefHypothesisKey Key;
        public short Score;
        public byte SupportingEvidence;
        public byte Contradictions;
        public long LastUpdatedTick;
        public WorldEventId LastCauseId;
        public WorldEventId LastEffectId;

        public readonly BeliefHypothesisSnapshot Snapshot =>
            new(Key, Score, SupportingEvidence, Contradictions, LastUpdatedTick);
    }

    private sealed class Mind
    {
        public VillagerDefinition Definition { get; }
        public Hypothesis[] Hypotheses { get; } = new Hypothesis[MaxHypothesesPerVillager];
        public PendingCause[] Pending { get; } = new PendingCause[MaxPendingCausesPerVillager];
        public int HypothesisCount;
        public int PendingCount;
        public ulong LastProcessedEventId;

        public Mind(VillagerDefinition definition) => Definition = definition;

        public int FindHypothesis(BeliefHypothesisKey key)
        {
            for (int i = 0; i < HypothesisCount; i++)
                if (Hypotheses[i].Key == key) return i;
            return -1;
        }

        public void ApplyPrior(BeliefHypothesisKey key, int score)
        {
            int index = GetOrCreate(key, 0);
            Hypotheses[index].Score = (short)Math.Clamp(score, -1000, 1000);
        }

        public void ApplySupport(
            BeliefHypothesisKey key,
            int evidence,
            WorldEventId causeId,
            WorldEventId effectId,
            long tick)
        {
            int index = GetOrCreate(key, tick);
            ref Hypothesis hypothesis = ref Hypotheses[index];
            hypothesis.Score = (short)Math.Clamp(hypothesis.Score + evidence * 4, -1000, 1000);
            hypothesis.SupportingEvidence = (byte)Math.Min(byte.MaxValue, hypothesis.SupportingEvidence + 1);
            hypothesis.LastUpdatedTick = tick;
            hypothesis.LastCauseId = causeId;
            hypothesis.LastEffectId = effectId;
        }

        public void ApplyContradiction(
            BeliefHypothesisKey key,
            int evidence,
            WorldEventId causeId,
            long tick)
        {
            int index = GetOrCreate(key, tick);
            ref Hypothesis hypothesis = ref Hypotheses[index];
            hypothesis.Score = (short)Math.Clamp(hypothesis.Score - evidence * 3, -1000, 1000);
            hypothesis.Contradictions = (byte)Math.Min(byte.MaxValue, hypothesis.Contradictions + 1);
            hypothesis.LastUpdatedTick = tick;
            hypothesis.LastCauseId = causeId;
            hypothesis.LastEffectId = default;
        }

        public void ApplyTestimony(BeliefHypothesisKey key, int score, long tick)
        {
            int index = GetOrCreate(key, tick);
            ref Hypothesis hypothesis = ref Hypotheses[index];
            hypothesis.Score = (short)Math.Clamp(hypothesis.Score + Math.Min(80, score), -1000, 1000);
            hypothesis.LastUpdatedTick = tick;
        }

        public void AddPending(int ruleIndex, in WorldObservation cause)
        {
            if (PendingCount == Pending.Length)
            {
                int earliest = 0;
                for (int i = 1; i < PendingCount; i++)
                    if (Pending[i].DeadlineTick < Pending[earliest].DeadlineTick) earliest = i;
                for (int i = earliest; i < PendingCount - 1; i++) Pending[i] = Pending[i + 1];
                PendingCount--;
            }
            Pending[PendingCount++] = new PendingCause
            {
                RuleIndex = ruleIndex,
                Cause = cause,
                DeadlineTick = cause.Tick + Rules[ruleIndex].WindowTicks
            };
        }

        private int GetOrCreate(BeliefHypothesisKey key, long tick)
        {
            int existing = FindHypothesis(key);
            if (existing >= 0) return existing;
            int index;
            if (HypothesisCount < Hypotheses.Length)
            {
                index = HypothesisCount++;
            }
            else
            {
                index = 0;
                for (int i = 1; i < HypothesisCount; i++)
                {
                    int left = Math.Abs(Hypotheses[i].Score);
                    int right = Math.Abs(Hypotheses[index].Score);
                    if (left < right || left == right && Hypotheses[i].LastUpdatedTick < Hypotheses[index].LastUpdatedTick ||
                        left == right && Hypotheses[i].LastUpdatedTick == Hypotheses[index].LastUpdatedTick &&
                        CompareKey(Hypotheses[i].Key, Hypotheses[index].Key) < 0)
                        index = i;
                }
            }
            Hypotheses[index] = new Hypothesis { Key = key, LastUpdatedTick = tick };
            return index;
        }

        private static int CompareKey(BeliefHypothesisKey left, BeliefHypothesisKey right)
        {
            int cause = left.Cause.CompareTo(right.Cause);
            return cause != 0 ? cause : left.Effect.CompareTo(right.Effect);
        }
    }
}
