namespace TheGodTheyMade.Simulation.Village;

using TheGodTheyMade.Simulation.Navigation;

public sealed class VillageDirector
{
    public VillagePhase GetPhase(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        int dayTick = (int)(tick % MingzhongVillage.TicksPerDay);
        return dayTick switch
        {
            < 60 * 60 => VillagePhase.Dawn,
            < 180 * 60 => VillagePhase.FirstWork,
            < 300 * 60 => VillagePhase.CrisisWork,
            < 390 * 60 => VillagePhase.MiddayGathering,
            < 510 * 60 => VillagePhase.SecondWork,
            < 570 * 60 => VillagePhase.DuskGathering,
            _ => VillagePhase.ReturnHome
        };
    }

    public VillageTaskAssignment GetAssignment(
        in VillagerDefinition villager,
        long tick,
        bool gateBlocked)
    {
        VillagePhase phase = GetPhase(tick);
        int sequence = (int)phase;
        return phase switch
        {
            VillagePhase.Dawn => Dawn(villager, sequence),
            VillagePhase.FirstWork => Work(villager, gateBlocked, sequence),
            VillagePhase.CrisisWork => Crisis(villager, gateBlocked, sequence),
            VillagePhase.MiddayGathering =>
                new VillageTaskAssignment(VillageTaskKind.Gather, MingzhongVillage.Square, sequence),
            VillagePhase.SecondWork => Crisis(villager, gateBlocked, sequence),
            VillagePhase.DuskGathering =>
                new VillageTaskAssignment(VillageTaskKind.Gather, MingzhongVillage.Square, sequence),
            _ => new VillageTaskAssignment(
                VillageTaskKind.ReturnHome, villager.Home, sequence)
        };
    }

    private static VillageTaskAssignment Dawn(in VillagerDefinition villager, int sequence) =>
        villager.Role is VillagerRole.BellKeeper or VillagerRole.BellApprentice
            ? new VillageTaskAssignment(VillageTaskKind.RingBell, MingzhongVillage.Bell, sequence)
            : new VillageTaskAssignment(VillageTaskKind.DepartHome, villager.Work, sequence);

    private static VillageTaskAssignment Work(
        in VillagerDefinition villager,
        bool gateBlocked,
        int sequence) => villager.Role switch
    {
        VillagerRole.Farmer => new(VillageTaskKind.InspectField, villager.Work, sequence),
        VillagerRole.CanalKeeper => new(VillageTaskKind.InspectGate, MingzhongVillage.Gate, sequence),
        VillagerRole.Mason or VillagerRole.Carrier or VillagerRole.Potter =>
            new(VillageTaskKind.WorkshopLabor, MingzhongVillage.Workshop, sequence),
        VillagerRole.Herder => new(VillageTaskKind.ObserveFamiliar, MingzhongVillage.FamiliarRest, sequence),
        VillagerRole.Ritualist or VillagerRole.Muralist =>
            new(VillageTaskKind.TendCemetery, MingzhongVillage.Cemetery, sequence),
        _ => new VillageTaskAssignment(VillageTaskKind.DepartHome, villager.Work, sequence)
    };

    private static VillageTaskAssignment Crisis(
        in VillagerDefinition villager,
        bool gateBlocked,
        int sequence)
    {
        if (gateBlocked && villager.Role is
            VillagerRole.CanalKeeper or VillagerRole.Mason or VillagerRole.Carrier)
        {
            return new VillageTaskAssignment(
                VillageTaskKind.ClearGate,
                MingzhongVillage.Gate,
                sequence);
        }
        return Work(villager, gateBlocked, sequence);
    }
}
