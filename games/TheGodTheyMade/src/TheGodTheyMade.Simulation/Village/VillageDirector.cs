namespace TheGodTheyMade.Simulation.Village;

using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Beliefs;

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
        bool gateBlocked,
        VillageBeliefBehavior belief = default)
    {
        VillagePhase phase = GetPhase(tick);
        int sequence = (int)phase;
        return phase switch
        {
            VillagePhase.Dawn => Dawn(villager, sequence),
            VillagePhase.FirstWork => Work(villager, gateBlocked, sequence),
            VillagePhase.CrisisWork => Crisis(villager, gateBlocked, sequence),
            VillagePhase.MiddayGathering => Gathering(belief, sequence),
            VillagePhase.SecondWork => BeliefWork(villager, gateBlocked, belief, sequence),
            VillagePhase.DuskGathering => Gathering(belief, sequence),
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

    private static VillageTaskAssignment Gathering(
        in VillageBeliefBehavior belief,
        int sequence) => new(
            belief.AttendDoctrineGathering ? VillageTaskKind.DoctrineGather : VillageTaskKind.Gather,
            MingzhongVillage.Square,
            sequence);

    private static VillageTaskAssignment BeliefWork(
        in VillagerDefinition villager,
        bool gateBlocked,
        in VillageBeliefBehavior belief,
        int sequence)
    {
        if (belief.PrioritizeBell && villager.Role is VillagerRole.BellKeeper or VillagerRole.Ritualist)
            return new VillageTaskAssignment(VillageTaskKind.RingBell, MingzhongVillage.Bell, sequence);
        if (belief.MaintainBell && villager.Role is VillagerRole.BellApprentice or VillagerRole.Mason)
            return new VillageTaskAssignment(VillageTaskKind.MaintainBell, MingzhongVillage.Bell, sequence);
        return Crisis(villager, gateBlocked, sequence);
    }

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
