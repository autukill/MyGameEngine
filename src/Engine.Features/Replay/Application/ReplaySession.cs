namespace GameEngine.Features.Replay.Application;

using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Features.Replay.Domain;
using GameEngine.Features.Replay.Infrastructure;

public enum ReplaySessionMode
{
    Recording,
    Playback
}

/// <summary>
/// Developer-facing owner that keeps logical input and gameplay-state recording in one lifecycle.
/// Recording sessions are saved after the application exits; playback sessions are immutable.
/// </summary>
public sealed class ReplaySession
{
    private readonly LogicalInputRecorder? _inputRecorder;
    private readonly GameplayStateRecorder? _stateRecorder;
    private readonly ReplayBundle? _bundle;

    public ReplaySessionMode Mode { get; }
    public ReplayIdentity Identity { get; }
    public LogicalInputRecorder? InputRecorder => _inputRecorder;
    public GameplayStateRecorder? StateRecorder => _stateRecorder;
    public ReplayBundle? Bundle => _bundle;

    private ReplaySession(
        ReplaySessionMode mode,
        ReplayIdentity identity,
        LogicalInputRecorder? inputRecorder,
        GameplayStateRecorder? stateRecorder,
        ReplayBundle? bundle)
    {
        Mode = mode;
        Identity = identity;
        _inputRecorder = inputRecorder;
        _stateRecorder = stateRecorder;
        _bundle = bundle;
    }

    public static ReplaySession Record(
        ReplayIdentity identity,
        int initialFrameCapacity = 0)
    {
        if (identity.GameId is null || identity.BuildId is null)
            throw new ArgumentException("Replay identity must be initialized.", nameof(identity));
        if (initialFrameCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialFrameCapacity));
        return new ReplaySession(
            ReplaySessionMode.Recording,
            identity,
            new LogicalInputRecorder(initialFrameCapacity),
            new GameplayStateRecorder(initialFrameCapacity),
            null);
    }

    public static ReplaySession Play(ReplayBundle bundle, ReplayIdentity expectedIdentity)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        bundle.ValidateIdentity(expectedIdentity);
        return new ReplaySession(
            ReplaySessionMode.Playback,
            bundle.Identity,
            null,
            null,
            bundle);
    }

    public static ReplaySession Load(
        string path,
        ReplayIdentity expectedIdentity,
        ReplayBundleLimits? limits = null) =>
        Play(ReplayBundleReader.Read(path, limits), expectedIdentity);

    public static ReplaySession Load(
        Stream source,
        ReplayIdentity expectedIdentity,
        ReplayBundleLimits? limits = null) =>
        Play(ReplayBundleReader.Read(source, limits), expectedIdentity);

    public ReplayBundle Snapshot()
    {
        RequireMode(ReplaySessionMode.Recording);
        return new ReplayBundle(
            Identity,
            _inputRecorder!.Snapshot(),
            _stateRecorder!.Snapshot());
    }

    public void Save(string path, ReplayBundleLimits? limits = null) =>
        ReplayBundleWriter.Write(path, Snapshot(), limits);

    public void Save(Stream destination, ReplayBundleLimits? limits = null) =>
        ReplayBundleWriter.Write(destination, Snapshot(), limits);

    public void RequireMode(ReplaySessionMode expected)
    {
        if (Mode != expected)
            throw new InvalidOperationException(
                $"Replay session is in {Mode} mode, but {expected} mode is required.");
    }
}
