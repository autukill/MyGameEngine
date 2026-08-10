namespace TheGodTheyMade.Simulation.Familiar;

public enum FamiliarSituation
{
    FireEmergency,
    VillagerInDanger,
    BlockedWaterGate,
    DryCropHoldingWater,
    DryCropNeedsWater,
    BellGathering,
    IdleVillage
}

public enum FamiliarAction
{
    FetchWater,
    PourWater,
    CarryObject,
    RingBell,
    ComfortVillager,
    Flee
}

[Flags]
public enum FamiliarActionMask
{
    None = 0,
    FetchWater = 1 << FamiliarAction.FetchWater,
    PourWater = 1 << FamiliarAction.PourWater,
    CarryObject = 1 << FamiliarAction.CarryObject,
    RingBell = 1 << FamiliarAction.RingBell,
    ComfortVillager = 1 << FamiliarAction.ComfortVillager,
    Flee = 1 << FamiliarAction.Flee,
    All = FetchWater | PourWater | CarryObject | RingBell | ComfortVillager | Flee
}

public enum FamiliarRewardReason
{
    None,
    PlayerPraise,
    PlayerStop,
    CropRecovered,
    FireExtinguished,
    GateOpened,
    VillagerRescued,
    VillagerInjured,
    NoEffect,
    AffordanceFailed,
    SafeDiscovery
}

public readonly record struct FamiliarPerception(
    bool HasReachableFire,
    bool HasVillagerInDanger,
    bool HasBlockedWaterGate,
    bool HasDryCrop,
    bool IsHoldingWater,
    bool CanLocateWater,
    bool AreVillagersGathered);

public readonly record struct FamiliarTemperament(
    byte Curiosity,
    byte Caution,
    byte Empathy,
    byte Autonomy)
{
    public static FamiliarTemperament Default => new(65, 45, 70, 55);

    public FamiliarTemperament Validate()
    {
        if (Curiosity > 100 || Caution > 100 || Empathy > 100 || Autonomy > 100)
            throw new ArgumentOutOfRangeException(nameof(FamiliarTemperament));
        return this;
    }
}

public readonly record struct FamiliarDecision(
    long Tick,
    FamiliarSituation Situation,
    FamiliarAction Action,
    int Score,
    bool Explored);

public readonly record struct FamiliarDecisionTrace(
    long Tick,
    FamiliarSituation Situation,
    FamiliarAction Action,
    FamiliarRewardReason Reason,
    int Reward,
    int PreviousQ,
    int NewQ,
    bool Explored);

public static class FamiliarSituationClassifier
{
    public static FamiliarSituation Classify(in FamiliarPerception perception)
    {
        if (perception.HasReachableFire) return FamiliarSituation.FireEmergency;
        if (perception.HasVillagerInDanger) return FamiliarSituation.VillagerInDanger;
        if (perception.HasBlockedWaterGate) return FamiliarSituation.BlockedWaterGate;
        if (perception.HasDryCrop && perception.IsHoldingWater) return FamiliarSituation.DryCropHoldingWater;
        if (perception.HasDryCrop && perception.CanLocateWater) return FamiliarSituation.DryCropNeedsWater;
        if (perception.AreVillagersGathered) return FamiliarSituation.BellGathering;
        return FamiliarSituation.IdleVillage;
    }
}

public static class ApeFamiliarBody
{
    public static FamiliarActionMask GetLegalActions(FamiliarSituation situation) => situation switch
    {
        FamiliarSituation.FireEmergency => FamiliarActionMask.FetchWater | FamiliarActionMask.Flee,
        FamiliarSituation.VillagerInDanger => FamiliarActionMask.ComfortVillager | FamiliarActionMask.Flee,
        FamiliarSituation.BlockedWaterGate => FamiliarActionMask.CarryObject | FamiliarActionMask.Flee,
        FamiliarSituation.DryCropHoldingWater => FamiliarActionMask.PourWater | FamiliarActionMask.Flee,
        FamiliarSituation.DryCropNeedsWater => FamiliarActionMask.FetchWater | FamiliarActionMask.Flee,
        FamiliarSituation.BellGathering => FamiliarActionMask.RingBell | FamiliarActionMask.ComfortVillager | FamiliarActionMask.Flee,
        _ => FamiliarActionMask.RingBell | FamiliarActionMask.Flee
    };

    public static bool Contains(FamiliarActionMask mask, FamiliarAction action) =>
        (mask & (FamiliarActionMask)(1 << (int)action)) != 0;
}
