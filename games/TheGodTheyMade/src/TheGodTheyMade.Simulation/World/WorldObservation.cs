namespace TheGodTheyMade.Simulation.World;

using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;

public readonly record struct WorldEventId(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum ObservationKind
{
    BellRang,
    RainStarted,
    RainEnded,
    CropWithered,
    CropRecovered,
    OfferingPlaced,
    FuneralStarted,
    FamiliarArrived,
    FamiliarActed,
    GateOpened,
    FireStarted,
    FireExtinguished,
    VillagerInjured
}

[Flags]
public enum ObservationChannel
{
    None = 0,
    Visual = 1,
    Auditory = 2,
    Direct = 4
}

public readonly record struct WorldObservation(
    WorldEventId Id,
    long Tick,
    ObservationKind Kind,
    ObservationChannel Channel,
    string SubjectId,
    string? TargetId,
    GridCell Cell,
    byte Salience);

public sealed class VillagerObservationMemory
{
    public const int Capacity = 32;
    private readonly WorldObservation[] _items = new WorldObservation[Capacity];
    private int _count;

    public VillagerId Villager { get; }
    public int Count => _count;
    public ReadOnlySpan<WorldObservation> Items => _items.AsSpan(0, _count);
    public WorldObservation this[int index] => index >= 0 && index < _count
        ? _items[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public VillagerObservationMemory(VillagerId villager) => Villager = villager;

    internal void Remember(in WorldObservation observation)
    {
        if (_count < Capacity)
        {
            _items[_count++] = observation;
            return;
        }

        int replace = 0;
        for (int i = 1; i < _count; i++)
        {
            ref readonly WorldObservation candidate = ref _items[i];
            ref readonly WorldObservation selected = ref _items[replace];
            if (candidate.Salience < selected.Salience ||
                candidate.Salience == selected.Salience && candidate.Tick < selected.Tick ||
                candidate.Salience == selected.Salience && candidate.Tick == selected.Tick &&
                candidate.Id.Value < selected.Id.Value)
            {
                replace = i;
            }
        }

        if (observation.Salience < _items[replace].Salience) return;
        for (int i = replace; i < _count - 1; i++) _items[i] = _items[i + 1];
        _items[^1] = observation;
    }
}
