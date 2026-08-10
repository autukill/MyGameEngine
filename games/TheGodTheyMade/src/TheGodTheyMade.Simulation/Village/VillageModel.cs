namespace TheGodTheyMade.Simulation.Village;

using TheGodTheyMade.Simulation.Navigation;

public readonly record struct VillagerId
{
    public string Value { get; }

    public VillagerId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

public enum VillagerRole
{
    BellKeeper,
    BellApprentice,
    Farmer,
    CanalKeeper,
    Mason,
    Ritualist,
    Healer,
    Muralist,
    Potter,
    Carrier,
    Herder
}

public enum VillagePhase
{
    Dawn,
    FirstWork,
    CrisisWork,
    MiddayGathering,
    SecondWork,
    DuskGathering,
    ReturnHome
}

public enum VillageTaskKind
{
    DepartHome,
    RingBell,
    InspectField,
    InspectGate,
    WorkshopLabor,
    ObserveFamiliar,
    TendCemetery,
    Gather,
    ClearGate,
    ReturnHome
}

public readonly record struct VillagerDefinition(
    VillagerId Id,
    string DisplayName,
    VillagerRole Role,
    GridCell Home,
    GridCell Work,
    byte ObservationReliability,
    byte SocialInfluence,
    byte TraditionBias,
    sbyte FamiliarAttitude);

public readonly record struct VillageTaskAssignment(
    VillageTaskKind Kind,
    GridCell Destination,
    int Sequence);

public static class MingzhongVillage
{
    public const int TicksPerSecond = 60;
    public const int TicksPerDay = 600 * TicksPerSecond;

    public static readonly GridCell Bell = new(8, 9);
    public static readonly GridCell Square = new(21, 16);
    public static readonly GridCell Gate = new(29, 11);
    public static readonly GridCell FamiliarRest = new(42, 16);
    public static readonly GridCell Cemetery = new(9, 28);
    public static readonly GridCell Workshop = new(15, 14);

    private static readonly VillagerDefinition[] Definitions =
    [
        New("cen_bellkeeper", "岑伯", VillagerRole.BellKeeper, 5, 14, Bell, 85, 95, 90, -10),
        New("musheng_farmer", "木生", VillagerRole.Farmer, 5, 14, new GridCell(29, 22), 80, 60, 55, 10),
        New("xiaohe_apprentice", "小禾", VillagerRole.BellApprentice, 5, 14, Bell, 70, 40, 30, 50),
        New("lan_canalkeeper", "澜姨", VillagerRole.CanalKeeper, 9, 18, Gate, 95, 80, 25, 20),
        New("sui_farmer", "阿穗", VillagerRole.Farmer, 9, 18, new GridCell(35, 23), 90, 55, 35, 30),
        New("li_mason", "砾", VillagerRole.Mason, 9, 18, Workshop, 85, 65, 30, 15),
        New("mian_ritualist", "眠婆", VillagerRole.Ritualist, 6, 23, Cemetery, 75, 90, 95, -20),
        New("du_healer", "渡", VillagerRole.Healer, 6, 23, Square, 90, 75, 40, 0),
        New("lu_muralist", "芦", VillagerRole.Muralist, 6, 23, Cemetery, 85, 70, 60, 25),
        New("tao_potter", "灰陶", VillagerRole.Potter, 14, 19, Workshop, 75, 50, 45, 10),
        New("yu_carrier", "榆", VillagerRole.Carrier, 14, 19, Workshop, 80, 45, 20, 40),
        New("dou_herder", "豆", VillagerRole.Herder, 14, 19, FamiliarRest, 65, 35, 15, 60)
    ];

    public static IReadOnlyList<VillagerDefinition> Roster => Definitions;

    private static VillagerDefinition New(
        string id,
        string name,
        VillagerRole role,
        int homeX,
        int homeY,
        GridCell work,
        byte observation,
        byte influence,
        byte tradition,
        sbyte familiar) =>
        new(new VillagerId(id), name, role, new GridCell(homeX, homeY), work,
            observation, influence, tradition, familiar);
}
