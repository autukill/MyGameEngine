namespace TheGodTheyMade.Simulation.Beliefs;

using TheGodTheyMade.Simulation.World;

public readonly record struct BeliefHypothesisKey(
    ObservationKind Cause,
    ObservationKind Effect);

public enum BeliefConviction
{
    Opposed,
    Undecided,
    Suspected,
    Believed,
    Advocated
}

public readonly record struct BeliefHypothesisSnapshot(
    BeliefHypothesisKey Key,
    short Score,
    byte SupportingEvidence,
    byte Contradictions,
    long LastUpdatedTick);

public readonly record struct PublicDoctrine(
    BeliefHypothesisKey Key,
    string AdvocateId,
    int Responders,
    long EstablishedTick);

public readonly record struct VillageBeliefBehavior(
    bool PrioritizeBell,
    bool MaintainBell,
    bool AttendDoctrineGathering);

public static class BeliefThresholds
{
    public static BeliefConviction Classify(int score) => score switch
    {
        < -200 => BeliefConviction.Opposed,
        < 100 => BeliefConviction.Undecided,
        < 300 => BeliefConviction.Suspected,
        < 450 => BeliefConviction.Believed,
        _ => BeliefConviction.Advocated
    };
}
