namespace GameEngine.Features.Replay.Domain;

using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;

/// <summary>Caller-owned identity used to reject replays from another game or build.</summary>
public readonly record struct ReplayIdentity
{
    public string GameId { get; }
    public string BuildId { get; }

    public ReplayIdentity(string gameId, string buildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        GameId = gameId;
        BuildId = buildId;
    }

    public override string ToString() => $"{GameId}@{BuildId}";
}

/// <summary>Hard limits applied before allocating arrays for an untrusted replay file.</summary>
public sealed record ReplayBundleLimits(
    long MaxFileBytes = 256L * 1024L * 1024L,
    int MaxFrames = 1_000_000,
    int MaxActions = 1024,
    int MaxAxes2D = 256,
    int MaxContributorsPerFrame = 100_000,
    int MaxStringBytes = 1024 * 1024)
{
    internal void Validate()
    {
        if (MaxFileBytes < 64 || MaxFileBytes > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(MaxFileBytes));
        if (MaxFrames <= 0) throw new ArgumentOutOfRangeException(nameof(MaxFrames));
        if (MaxActions < 0) throw new ArgumentOutOfRangeException(nameof(MaxActions));
        if (MaxAxes2D < 0) throw new ArgumentOutOfRangeException(nameof(MaxAxes2D));
        if (MaxContributorsPerFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(MaxContributorsPerFrame));
        if (MaxStringBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxStringBytes));
    }
}

/// <summary>
/// Immutable input and state trace for one complete deterministic run. A bundle intentionally does
/// not contain assets, physical bindings, screenshots, or restorable gameplay snapshots.
/// </summary>
public sealed class ReplayBundle
{
    public const int CurrentFormatVersion = 1;

    public ReplayIdentity Identity { get; }
    public LogicalInputRecording Input { get; }
    public GameplayStateRecording GameplayState { get; }
    public int FormatVersion => CurrentFormatVersion;
    public int FrameCount => Input.FrameCount;
    public double FixedDeltaSeconds => GameplayState.FixedDeltaSeconds;

    public ReplayBundle(
        ReplayIdentity identity,
        LogicalInputRecording input,
        GameplayStateRecording gameplayState)
    {
        if (identity.GameId is null || identity.BuildId is null)
            throw new ArgumentException("Replay identity must be initialized.", nameof(identity));
        Identity = identity;
        Input = input ?? throw new ArgumentNullException(nameof(input));
        GameplayState = gameplayState ?? throw new ArgumentNullException(nameof(gameplayState));
        ValidateRecordings(input, gameplayState);
    }

    public void ValidateIdentity(ReplayIdentity expected)
    {
        if (expected.GameId is null || expected.BuildId is null)
            throw new ArgumentException("Expected replay identity must be initialized.", nameof(expected));
        if (Identity != expected)
        {
            throw new InvalidOperationException(
                $"Replay identity '{Identity}' does not match expected '{expected}'.");
        }
    }

    private static void ValidateRecordings(
        LogicalInputRecording input,
        GameplayStateRecording gameplayState)
    {
        if (input.FrameCount == 0 || gameplayState.SnapshotCount == 0)
            throw new ArgumentException("A replay bundle requires at least one complete Tick.");
        if (input.FixedDeltaSeconds is not { } inputDelta ||
            BitConverter.DoubleToInt64Bits(inputDelta) !=
            BitConverter.DoubleToInt64Bits(gameplayState.FixedDeltaSeconds))
        {
            throw new ArgumentException(
                "Input and gameplay state recordings must use the same fixed delta.");
        }
        if (input.FrameCount != gameplayState.SnapshotCount ||
            input.FirstStepIndex != gameplayState.FirstStepIndex ||
            input.LastStepIndex != gameplayState.LastStepIndex)
        {
            throw new ArgumentException(
                "Input frames and gameplay state snapshots must cover the same Tick range.");
        }
        if (input.FirstStepIndex != 1)
            throw new ArgumentException("A complete replay bundle must begin at Tick 1.");

        for (int i = 0; i < input.FrameCount; i++)
        {
            if (input.Frames[i].StepIndex != (ulong)i + 1UL ||
                gameplayState.Snapshots[i].StepIndex != (ulong)i + 1UL)
            {
                throw new ArgumentException("Replay Tick indices must be contiguous from Tick 1.");
            }
        }
    }
}
