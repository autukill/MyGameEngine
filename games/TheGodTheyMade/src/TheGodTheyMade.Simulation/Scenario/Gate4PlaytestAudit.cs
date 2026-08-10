namespace TheGodTheyMade.Simulation.Scenario;

public readonly record struct Gate4MuralHistory(
    string Awakening,
    string Guardian,
    string Cost);

public readonly record struct Gate4Questionnaire(
    bool? ExplainedBeliefEvidence,
    bool? RecognizedWaitingAsChoice,
    bool? LinkedFamiliarActionToTeaching,
    bool? DiscoveredWetRuin,
    string? FuneralChoiceAndTradeoff,
    string? RetoldMural,
    string? ConfusionAndBlockers)
{
    public bool IsComplete =>
        ExplainedBeliefEvidence.HasValue &&
        RecognizedWaitingAsChoice.HasValue &&
        LinkedFamiliarActionToTeaching.HasValue &&
        DiscoveredWetRuin.HasValue &&
        !string.IsNullOrWhiteSpace(FuneralChoiceAndTradeoff) &&
        !string.IsNullOrWhiteSpace(RetoldMural) &&
        !string.IsNullOrWhiteSpace(ConfusionAndBlockers);
}

public readonly record struct Gate4PlaytestEvidence(
    string TesterId,
    bool Completed,
    Gate4MuralHistory? Mural,
    Gate4Questionnaire Questionnaire);

public sealed record Gate4PlaytestAuditResult(
    bool Passed,
    int PlayerCount,
    int CompletedCount,
    int CompleteQuestionnaireCount,
    int ExplainedBeliefEvidenceCount,
    int RecognizedWaitingAsChoiceCount,
    int LinkedFamiliarActionToTeachingCount,
    int DiscoveredWetRuinCount,
    int DistinctMuralHistoryCount,
    int RequiredBeliefEvidenceCount,
    int RequiredWaitingChoiceCount,
    int RequiredFamiliarTeachingCount,
    IReadOnlyList<string> Failures);

public static class Gate4PlaytestAudit
{
    public const int MinimumPlayers = 5;

    public static Gate4PlaytestAuditResult Evaluate(IEnumerable<Gate4PlaytestEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Gate4PlaytestEvidence[] sessions = evidence.ToArray();
        var testerIds = new HashSet<string>(StringComparer.Ordinal);
        var muralHistories = new HashSet<Gate4MuralHistory>();
        int completed = 0;
        int completeQuestionnaires = 0;
        int explainedBelief = 0;
        int recognizedWaiting = 0;
        int linkedFamiliar = 0;
        int discoveredRuin = 0;

        for (int i = 0; i < sessions.Length; i++)
        {
            ref readonly Gate4PlaytestEvidence session = ref sessions[i];
            if (string.IsNullOrWhiteSpace(session.TesterId))
                throw new ArgumentException("Every playtest requires a non-empty tester id.", nameof(evidence));
            if (!testerIds.Add(session.TesterId))
                throw new ArgumentException($"Duplicate tester id '{session.TesterId}'.", nameof(evidence));
            if (session.Completed) completed++;
            if (session.Mural is { } mural) muralHistories.Add(mural);
            if (session.Questionnaire.IsComplete) completeQuestionnaires++;
            if (session.Questionnaire.ExplainedBeliefEvidence == true) explainedBelief++;
            if (session.Questionnaire.RecognizedWaitingAsChoice == true) recognizedWaiting++;
            if (session.Questionnaire.LinkedFamiliarActionToTeaching == true) linkedFamiliar++;
            if (session.Questionnaire.DiscoveredWetRuin == true) discoveredRuin++;
        }

        int denominator = Math.Max(MinimumPlayers, sessions.Length);
        int requiredBelief = CeilingRatio(denominator, 4, 5);
        int requiredWaiting = CeilingRatio(denominator, 3, 5);
        int requiredFamiliar = CeilingRatio(denominator, 4, 5);
        var failures = new List<string>(6);
        if (sessions.Length < MinimumPlayers)
            failures.Add($"需要至少 {MinimumPlayers} 名不同测试员，当前为 {sessions.Length} 名。");
        if (completed != sessions.Length || completed < MinimumPlayers)
            failures.Add($"至少 {MinimumPlayers} 名测试员必须完成章节，当前完成 {completed}/{sessions.Length}。");
        if (completeQuestionnaires != sessions.Length)
            failures.Add($"每份报告必须补全七项人工问卷，当前完整 {completeQuestionnaires}/{sessions.Length}。");
        if (explainedBelief < requiredBelief)
            failures.Add($"解释信仰证据需要 {requiredBelief}/{denominator}，当前为 {explainedBelief}。");
        if (recognizedWaiting < requiredWaiting)
            failures.Add($"认可等待/不回应需要 {requiredWaiting}/{denominator}，当前为 {recognizedWaiting}。");
        if (linkedFamiliar < requiredFamiliar)
            failures.Add($"联系神兽行为与教导需要 {requiredFamiliar}/{denominator}，当前为 {linkedFamiliar}。");
        if (muralHistories.Count < 2)
            failures.Add($"至少需要 2 种不同壁画历史，当前为 {muralHistories.Count} 种。");

        return new Gate4PlaytestAuditResult(
            failures.Count == 0,
            sessions.Length,
            completed,
            completeQuestionnaires,
            explainedBelief,
            recognizedWaiting,
            linkedFamiliar,
            discoveredRuin,
            muralHistories.Count,
            requiredBelief,
            requiredWaiting,
            requiredFamiliar,
            failures);
    }

    private static int CeilingRatio(int value, int numerator, int denominator) =>
        (value * numerator + denominator - 1) / denominator;
}
