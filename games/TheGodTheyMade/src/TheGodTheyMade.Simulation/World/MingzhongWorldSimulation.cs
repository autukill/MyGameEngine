namespace TheGodTheyMade.Simulation.World;

using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;

public enum ReservoirLevel
{
    Empty,
    Low,
    Ready
}

public enum GateState
{
    Blocked,
    Open
}

public enum CanalState
{
    Dry,
    Filling,
    Flowing
}

public enum MingzhongCommandKind
{
    InvokeRain,
    OpenGate,
    RingBell
}

public readonly record struct MingzhongCommand(
    long Tick,
    MingzhongCommandKind Kind,
    GridCell Target,
    byte RadiusCells = 6)
{
    public static MingzhongCommand Rain(long tick, GridCell target, byte radiusCells = 6) =>
        new(tick, MingzhongCommandKind.InvokeRain, target, radiusCells);

    public static MingzhongCommand OpenGate(long tick) =>
        new(tick, MingzhongCommandKind.OpenGate, MingzhongVillage.Gate, 0);

    public static MingzhongCommand RingBell(long tick) =>
        new(tick, MingzhongCommandKind.RingBell, MingzhongVillage.Bell, 0);
}

public readonly record struct FieldSnapshot(
    string Id,
    GridCell Center,
    byte Moisture,
    bool Withered);

public sealed class MingzhongWorldSimulation
{
    public const int MaxGodIntent = 3;
    public const int InitialGodIntent = 2;
    public const int RainDurationTicks = 5 * MingzhongVillage.TicksPerSecond;
    public const int IntentRecoveryTicks = 45 * MingzhongVillage.TicksPerSecond;
    public const int CanalFillTicks = 3 * MingzhongVillage.TicksPerSecond;
    private const int GlobalLogCapacity = 256;

    private static readonly EventRule[] EventRules =
    [
        new(ObservationKind.BellRang, ObservationChannel.Auditory, 90, 20),
        new(ObservationKind.RainStarted, ObservationChannel.Visual | ObservationChannel.Auditory, 100, 12),
        new(ObservationKind.RainEnded, ObservationChannel.Visual, 55, 10),
        new(ObservationKind.CropWithered, ObservationChannel.Visual, 75, 6),
        new(ObservationKind.CropRecovered, ObservationChannel.Visual, 90, 6),
        new(ObservationKind.OfferingPlaced, ObservationChannel.Visual, 65, 5),
        new(ObservationKind.FuneralStarted, ObservationChannel.Visual | ObservationChannel.Auditory, 85, 10),
        new(ObservationKind.FamiliarArrived, ObservationChannel.Visual, 70, 7),
        new(ObservationKind.FamiliarActed, ObservationChannel.Visual, 80, 7),
        new(ObservationKind.GateOpened, ObservationChannel.Visual | ObservationChannel.Auditory, 85, 8),
        new(ObservationKind.FireStarted, ObservationChannel.Visual, 95, 10),
        new(ObservationKind.FireExtinguished, ObservationChannel.Visual, 90, 8),
        new(ObservationKind.VillagerInjured, ObservationChannel.Visual | ObservationChannel.Direct, 100, 8)
    ];

    private readonly Func<GridCell, bool> _blocksSight;
    private readonly VillagerRuntime[] _villagers;
    private readonly FieldRuntime[] _fields =
    [
        new("field.west", new GridCell(29, 22), 28),
        new("field.middle", new GridCell(35, 23), 22),
        new("field.east", new GridCell(41, 24), 18)
    ];
    private readonly WorldObservation[] _globalLog = new WorldObservation[GlobalLogCapacity];
    private int _globalCount;
    private ulong _nextEventId = 1;
    private int _intentRecoveryProgress;
    private int _rainTicksRemaining;
    private GridCell _rainCenter;
    private byte _rainRadius;
    private int _canalFillProgress;
    private int _reservoirUnits = 35;

    public long Tick { get; private set; }
    public int GodIntent { get; private set; } = InitialGodIntent;
    public ReservoirLevel Reservoir => _reservoirUnits switch
    {
        <= 0 => ReservoirLevel.Empty,
        < 70 => ReservoirLevel.Low,
        _ => ReservoirLevel.Ready
    };
    public int ReservoirUnits => _reservoirUnits;
    public GateState Gate { get; private set; } = GateState.Blocked;
    public CanalState Canal { get; private set; } = CanalState.Dry;
    public bool IsRaining => _rainTicksRemaining > 0;
    public GridCell RainCenter => _rainCenter;
    public byte RainRadiusCells => _rainRadius;
    public int FieldCount => _fields.Length;
    public int ObservationCount => _globalCount;
    public ReadOnlySpan<WorldObservation> Observations => _globalLog.AsSpan(0, _globalCount);

    public MingzhongWorldSimulation(
        IReadOnlyList<VillagerDefinition> villagers,
        Func<GridCell, bool>? blocksSight = null)
    {
        ArgumentNullException.ThrowIfNull(villagers);
        _blocksSight = blocksSight ?? NeverBlocksSight;
        _villagers = new VillagerRuntime[villagers.Count];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < villagers.Count; i++)
        {
            VillagerDefinition definition = villagers[i];
            if (!ids.Add(definition.Id.Value))
                throw new ArgumentException($"Duplicate villager ID '{definition.Id}'.", nameof(villagers));
            _villagers[i] = new VillagerRuntime(
                definition,
                new VillagerObservationMemory(definition.Id));
        }
    }

    public FieldSnapshot GetField(int index)
    {
        if ((uint)index >= (uint)_fields.Length) throw new ArgumentOutOfRangeException(nameof(index));
        ref readonly FieldRuntime field = ref _fields[index];
        return new FieldSnapshot(field.Id, field.Center, field.Moisture, field.Withered);
    }

    public VillagerObservationMemory GetMemory(VillagerId villager)
    {
        int index = FindVillager(villager);
        if (index < 0) throw new KeyNotFoundException($"Unknown villager '{villager}'.");
        return _villagers[index].Memory;
    }

    public void SetVillagerCell(VillagerId villager, GridCell cell)
    {
        int index = FindVillager(villager);
        if (index < 0) throw new KeyNotFoundException($"Unknown villager '{villager}'.");
        _villagers[index].Cell = cell;
    }

    public bool TryApply(in MingzhongCommand command)
    {
        if (command.Tick != Tick) return false;
        return command.Kind switch
        {
            MingzhongCommandKind.InvokeRain => TryInvokeRain(command.Target, command.RadiusCells),
            MingzhongCommandKind.OpenGate => TryOpenGate(),
            MingzhongCommandKind.RingBell => TryRingBell(),
            _ => false
        };
    }

    public void AdvanceTick()
    {
        if (Tick == 35L * MingzhongVillage.TicksPerSecond ||
            Tick == 7L * 60 * MingzhongVillage.TicksPerSecond)
        {
            Publish(ObservationKind.BellRang, "bell.mingzhong", null, MingzhongVillage.Bell);
        }

        UpdateIntent();
        UpdateRain();
        UpdateCanal();
        EvaluateFields();
        Tick++;
    }

    public WorldObservation Publish(
        ObservationKind kind,
        string subjectId,
        string? targetId,
        GridCell cell)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        EventRule rule = RuleFor(kind);
        var observation = new WorldObservation(
            new WorldEventId(_nextEventId++), Tick, kind, rule.Channel,
            subjectId, targetId, cell, rule.Salience);
        AppendGlobal(observation);
        Dispatch(observation, rule.Range);
        return observation;
    }

    public ulong ComputeStateHash()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        Add(ref hash, unchecked((ulong)Tick), prime);
        Add(ref hash, (ulong)GodIntent, prime);
        Add(ref hash, (ulong)_reservoirUnits, prime);
        Add(ref hash, (ulong)Gate, prime);
        Add(ref hash, (ulong)Canal, prime);
        Add(ref hash, (ulong)_rainTicksRemaining, prime);
        Add(ref hash, _nextEventId, prime);
        for (int i = 0; i < _fields.Length; i++)
        {
            Add(ref hash, _fields[i].Moisture, prime);
            Add(ref hash, _fields[i].Withered ? 1UL : 0UL, prime);
        }
        for (int i = 0; i < _villagers.Length; i++)
        {
            VillagerObservationMemory memory = _villagers[i].Memory;
            Add(ref hash, (ulong)memory.Count, prime);
            foreach (ref readonly WorldObservation observation in memory.Items)
                Add(ref hash, observation.Id.Value, prime);
        }
        return hash;
    }

    private bool TryInvokeRain(GridCell target, byte radius)
    {
        if (GodIntent <= 0 || IsRaining || radius is 0 or > 24) return false;
        GodIntent--;
        _intentRecoveryProgress = 0;
        _rainCenter = target;
        _rainRadius = radius;
        _rainTicksRemaining = RainDurationTicks;
        Publish(ObservationKind.RainStarted, "god.rain", null, target);
        return true;
    }

    private bool TryOpenGate()
    {
        if (Gate == GateState.Open) return false;
        Gate = GateState.Open;
        Publish(ObservationKind.GateOpened, "gate.mingzhong", null, MingzhongVillage.Gate);
        return true;
    }

    private bool TryRingBell()
    {
        Publish(ObservationKind.BellRang, "bell.mingzhong", null, MingzhongVillage.Bell);
        return true;
    }

    private void UpdateIntent()
    {
        if (GodIntent >= MaxGodIntent)
        {
            _intentRecoveryProgress = 0;
            return;
        }
        if (++_intentRecoveryProgress < IntentRecoveryTicks) return;
        _intentRecoveryProgress = 0;
        GodIntent++;
    }

    private void UpdateRain()
    {
        if (_rainTicksRemaining <= 0) return;
        int elapsed = RainDurationTicks - _rainTicksRemaining;
        if (elapsed % 15 == 0)
        {
            if (CircleTouchesRect(_rainCenter, _rainRadius, 21, 2, 16, 5))
                _reservoirUnits = Math.Min(100, _reservoirUnits + 2);
            for (int i = 0; i < _fields.Length; i++)
            {
                ref FieldRuntime field = ref _fields[i];
                if (DistanceSquared(_rainCenter, field.Center) <= _rainRadius * _rainRadius)
                    field.Moisture = (byte)Math.Min(100, field.Moisture + 1);
            }
        }

        _rainTicksRemaining--;
        if (_rainTicksRemaining == 0)
            Publish(ObservationKind.RainEnded, "god.rain", null, _rainCenter);
    }

    private void UpdateCanal()
    {
        if (Gate == GateState.Open && Reservoir == ReservoirLevel.Ready)
        {
            if (Canal == CanalState.Dry) Canal = CanalState.Filling;
            if (Canal == CanalState.Filling && ++_canalFillProgress >= CanalFillTicks)
                Canal = CanalState.Flowing;
        }
        else
        {
            Canal = CanalState.Dry;
            _canalFillProgress = 0;
        }

        if (Canal != CanalState.Flowing || Tick % MingzhongVillage.TicksPerSecond != 0) return;
        for (int i = 0; i < _fields.Length; i++)
            _fields[i].Moisture = (byte)Math.Min(100, _fields[i].Moisture + 1);
    }

    private void EvaluateFields()
    {
        for (int i = 0; i < _fields.Length; i++)
        {
            ref FieldRuntime field = ref _fields[i];
            if (!field.Withered && Tick >= 42L * MingzhongVillage.TicksPerSecond && field.Moisture < 20)
            {
                field.Withered = true;
                Publish(ObservationKind.CropWithered, field.Id, null, field.Center);
            }
            else if (field.Withered && field.Moisture >= 25)
            {
                field.Withered = false;
                Publish(ObservationKind.CropRecovered, field.Id, null, field.Center);
            }
        }
    }

    private void Dispatch(in WorldObservation observation, int range)
    {
        int rangeSquared = range * range;
        for (int i = 0; i < _villagers.Length; i++)
        {
            ref VillagerRuntime villager = ref _villagers[i];
            bool perceived = false;
            if ((observation.Channel & ObservationChannel.Direct) != 0 &&
                string.Equals(observation.TargetId, villager.Definition.Id.Value, StringComparison.Ordinal))
                perceived = true;
            int distanceSquared = DistanceSquared(villager.Cell, observation.Cell);
            if (!perceived && (observation.Channel & ObservationChannel.Auditory) != 0)
                perceived = distanceSquared <= rangeSquared;
            if (!perceived && (observation.Channel & ObservationChannel.Visual) != 0)
                perceived = distanceSquared <= rangeSquared && HasLineOfSight(villager.Cell, observation.Cell);
            if (perceived) villager.Memory.Remember(observation);
        }
    }

    private bool HasLineOfSight(GridCell from, GridCell to)
    {
        int x = from.X;
        int y = from.Y;
        int dx = Math.Abs(to.X - x);
        int sx = x < to.X ? 1 : -1;
        int dy = -Math.Abs(to.Y - y);
        int sy = y < to.Y ? 1 : -1;
        int error = dx + dy;
        while (x != to.X || y != to.Y)
        {
            int twice = error * 2;
            if (twice >= dy) { error += dy; x += sx; }
            if (twice <= dx) { error += dx; y += sy; }
            if ((x != to.X || y != to.Y) && _blocksSight(new GridCell(x, y))) return false;
        }
        return true;
    }

    private void AppendGlobal(in WorldObservation observation)
    {
        if (_globalCount < _globalLog.Length)
        {
            _globalLog[_globalCount++] = observation;
            return;
        }
        Array.Copy(_globalLog, 1, _globalLog, 0, _globalLog.Length - 1);
        _globalLog[^1] = observation;
    }

    private int FindVillager(VillagerId villager)
    {
        for (int i = 0; i < _villagers.Length; i++)
            if (_villagers[i].Definition.Id == villager) return i;
        return -1;
    }

    private static EventRule RuleFor(ObservationKind kind)
    {
        for (int i = 0; i < EventRules.Length; i++)
            if (EventRules[i].Kind == kind) return EventRules[i];
        throw new ArgumentOutOfRangeException(nameof(kind));
    }

    private static bool CircleTouchesRect(GridCell center, int radius, int x, int y, int width, int height)
    {
        int closestX = Math.Clamp(center.X, x, x + width - 1);
        int closestY = Math.Clamp(center.Y, y, y + height - 1);
        int dx = center.X - closestX;
        int dy = center.Y - closestY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static int DistanceSquared(GridCell left, GridCell right)
    {
        int dx = left.X - right.X;
        int dy = left.Y - right.Y;
        return dx * dx + dy * dy;
    }

    private static void Add(ref ulong hash, ulong value, ulong prime)
    {
        hash ^= value;
        hash *= prime;
    }

    private static bool NeverBlocksSight(GridCell _) => false;

    private readonly record struct EventRule(
        ObservationKind Kind,
        ObservationChannel Channel,
        byte Salience,
        int Range);

    private sealed class VillagerRuntime
    {
        public VillagerDefinition Definition { get; }
        public VillagerObservationMemory Memory { get; }
        public GridCell Cell;

        public VillagerRuntime(VillagerDefinition definition, VillagerObservationMemory memory)
        {
            Definition = definition;
            Memory = memory;
            Cell = definition.Home;
        }
    }

    private struct FieldRuntime
    {
        public string Id;
        public GridCell Center;
        public byte Moisture;
        public bool Withered;

        public FieldRuntime(string id, GridCell center, byte moisture)
        {
            Id = id;
            Center = center;
            Moisture = moisture;
            Withered = false;
        }
    }
}
