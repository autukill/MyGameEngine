namespace TheGodTheyMade.Game;

using System.Text.Json;
using System.Text.Json.Serialization;
using TheGodTheyMade.Simulation.Beliefs;
using TheGodTheyMade.Simulation.Familiar;
using TheGodTheyMade.Simulation.Scenario;
using TheGodTheyMade.Simulation.World;

internal static class MingzhongCommandRecordingFormat
{
    private const string GameId = "the-god-they-made.mingzhong";
    private const int MaxCommandCount = 100_000;
    private const long MaxFileBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static MingzhongCommandRecording Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Command recording was not found.", path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException($"Command recording exceeds {MaxFileBytes} bytes.");
        using FileStream stream = File.OpenRead(info.FullName);
        CommandFile file = JsonSerializer.Deserialize<CommandFile>(stream, JsonOptions)
            ?? throw new InvalidDataException("Command recording is empty.");
        if (file.SchemaVersion != MingzhongCommandRecording.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported command schema version {file.SchemaVersion}.");
        if (!string.Equals(file.GameId, GameId, StringComparison.Ordinal))
            throw new InvalidDataException($"Command recording belongs to '{file.GameId}'.");
        if (file.Commands is null || file.Commands.Length > MaxCommandCount)
            throw new InvalidDataException("Command recording has an invalid command count.");
        try
        {
            return new MingzhongCommandRecording(file.EndTick, file.Commands);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Command recording contains invalid commands.", exception);
        }
    }

    public static void Write(string path, MingzhongCommandRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (recording.Count > MaxCommandCount)
            throw new InvalidOperationException($"Command recording exceeds {MaxCommandCount} commands.");
        AtomicJson.Write(path, new CommandFile(
            recording.SchemaVersion,
            GameId,
            recording.EndTick,
            recording.Commands.ToArray()), JsonOptions);
    }

    private sealed record CommandFile(
        int SchemaVersion,
        string GameId,
        long EndTick,
        MingzhongCommand[] Commands);
}

internal static class PlaytestReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(
        string path,
        string testerId,
        MingzhongWorldSimulation world,
        BeliefSimulation beliefs,
        FamiliarLearning familiar,
        MingzhongIslandScenario scenario,
        MingzhongCommandJournal journal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testerId);
        var fields = new FieldReport[world.FieldCount];
        for (int i = 0; i < fields.Length; i++)
        {
            FieldSnapshot field = world.GetField(i);
            fields[i] = new FieldReport(field.Id, field.Moisture, field.Withered);
        }
        var traces = new FamiliarDecisionTrace[familiar.TraceCount];
        for (int i = 0; i < traces.Length; i++) traces[i] = familiar.GetTrace(i);

        PublicDoctrine? doctrine = beliefs.Doctrine;
        MuralTriptych? mural = scenario.Mural;
        var report = new PlaytestReport(
            SchemaVersion: 1,
            GameId: "the-god-they-made.mingzhong",
            TesterId: testerId,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Tick: world.Tick,
            Completed: scenario.IsComplete,
            Phase: scenario.Phase.ToString(),
            Ending: scenario.Ending?.ToString(),
            GateResolution: scenario.GateResolution.ToString(),
            Ruin: scenario.Ruin.ToString(),
            Funeral: scenario.Funeral.ToString(),
            RainCommandCount: world.AcceptedRainCommandCount,
            FirstRainTick: world.FirstRainTick,
            GateOpenedTick: world.GateOpenedTick,
            Fields: fields,
            Doctrine: doctrine is null ? null : new DoctrineReport(
                doctrine.Value.Key.Cause.ToString(),
                doctrine.Value.Key.Effect.ToString(),
                doctrine.Value.AdvocateId,
                doctrine.Value.Responders,
                doctrine.Value.EstablishedTick),
            Mural: mural is null ? null : new MuralReport(
                mural.Value.Awakening,
                mural.Value.Guardian,
                mural.Value.Cost),
            FamiliarTrace: traces,
            CommandJournalMode: journal.Mode.ToString(),
            CommandCount: journal.Count,
            WorldHash: world.ComputeStateHash(),
            BeliefHash: beliefs.ComputeStateHash(),
            FamiliarHash: familiar.ComputeStateHash(),
            ScenarioHash: scenario.ComputeStateHash(),
            Questionnaire: new PlaytestQuestionnaire(null, null, null, null, null, null, null));
        AtomicJson.Write(path, report, JsonOptions);
    }

    private sealed record PlaytestReport(
        int SchemaVersion,
        string GameId,
        string TesterId,
        DateTimeOffset RecordedAtUtc,
        long Tick,
        bool Completed,
        string Phase,
        string? Ending,
        string GateResolution,
        string Ruin,
        string Funeral,
        int RainCommandCount,
        long? FirstRainTick,
        long? GateOpenedTick,
        FieldReport[] Fields,
        DoctrineReport? Doctrine,
        MuralReport? Mural,
        FamiliarDecisionTrace[] FamiliarTrace,
        string CommandJournalMode,
        int CommandCount,
        ulong WorldHash,
        ulong BeliefHash,
        ulong FamiliarHash,
        ulong ScenarioHash,
        PlaytestQuestionnaire Questionnaire);

    private sealed record FieldReport(string Id, byte Moisture, bool Withered);
    private sealed record DoctrineReport(string Cause, string Effect, string AdvocateId, int Responders, long EstablishedTick);
    private sealed record MuralReport(string Awakening, string Guardian, string Cost);
    private sealed record PlaytestQuestionnaire(
        bool? ExplainedBeliefEvidence,
        bool? RecognizedWaitingAsChoice,
        bool? LinkedFamiliarActionToTeaching,
        bool? DiscoveredWetRuin,
        string? FuneralChoiceAndTradeoff,
        string? RetoldMural,
        string? ConfusionAndBlockers);
}

internal static class AtomicJson
{
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Output path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
