namespace GameEngine.Core.Domain.Gameplay;

using System.Numerics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Versioned, allocation-free FNV-1a writer for explicit deterministic gameplay state. Values are
/// encoded with type markers and little-endian IEEE/integer bits; field order is part of the schema.
/// </summary>
public struct GameplayStateWriter
{
    public const int AlgorithmVersion = 1;
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private ulong _hash;
    private bool _initialized;

    public readonly ulong Hash => _hash;

    public GameplayStateWriter()
    {
        _hash = OffsetBasis;
        _initialized = true;
    }

    public void Write(string name, bool value)
    {
        WriteField(name, 1);
        AppendByte(value ? (byte)1 : (byte)0);
    }

    public void Write(string name, int value)
    {
        WriteField(name, 2);
        AppendUInt32(unchecked((uint)value));
    }

    public void Write(string name, long value)
    {
        WriteField(name, 3);
        AppendUInt64(unchecked((ulong)value));
    }

    public void Write(string name, ulong value)
    {
        WriteField(name, 4);
        AppendUInt64(value);
    }

    public void Write(string name, float value)
    {
        WriteField(name, 5);
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    public void Write(string name, double value)
    {
        WriteField(name, 6);
        AppendUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));
    }

    public void Write(string name, string? value)
    {
        WriteField(name, 7);
        AppendString(value);
    }

    public void Write(string name, Vector2D value)
    {
        WriteField(name, 8);
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.X)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.Y)));
    }

    public void Write(string name, Vector4 value)
    {
        WriteField(name, 9);
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.X)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.Y)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.Z)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.W)));
    }

    public void Write(string name, Transform2D value)
    {
        WriteField(name, 10);
        AppendVector2(value.Position);
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.Rotation)));
        AppendVector2(value.Scale);
    }

    public void Write(string name, GameplayRandomState value) => Write(name, value.Value);

    public void Write(string name, GameplayHealth value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteField(name, 11);
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.MaximumHealth)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.CurrentHealth)));
    }

    public void Write(string name, GameplayCooldown value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteField(name, 12);
        AppendUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.DurationSeconds)));
        AppendUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.RemainingSeconds)));
    }

    public void Write(string name, InputActionBuffer value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteField(name, 13);
        AppendString(value.Action.Name);
        AppendUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.WindowSeconds)));
        AppendUInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(value.RemainingSeconds)));
    }

    private void WriteField(string name, byte type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureInitialized();
        AppendByte(0xF0);
        AppendString(name);
        AppendByte(type);
    }

    private void AppendVector2(Vector2D value)
    {
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.X)));
        AppendUInt32(unchecked((uint)BitConverter.SingleToInt32Bits(value.Y)));
    }

    private void AppendString(string? value)
    {
        if (value is null)
        {
            AppendUInt32(uint.MaxValue);
            return;
        }
        AppendUInt32((uint)value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            AppendByte((byte)character);
            AppendByte((byte)(character >> 8));
        }
    }

    private void AppendUInt32(uint value)
    {
        AppendByte((byte)value);
        AppendByte((byte)(value >> 8));
        AppendByte((byte)(value >> 16));
        AppendByte((byte)(value >> 24));
    }

    private void AppendUInt64(ulong value)
    {
        AppendUInt32((uint)value);
        AppendUInt32((uint)(value >> 32));
    }

    private void AppendByte(byte value)
    {
        EnsureInitialized();
        _hash ^= value;
        _hash *= Prime;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _hash = OffsetBasis;
        _initialized = true;
    }
}

public readonly record struct GameplayStateContributor(
    long Sequence,
    string Kind,
    ulong Hash);

/// <summary>Immutable Scene gameplay state captured after one committed simulation Step.</summary>
public sealed class GameplayStateSnapshot
{
    private readonly GameplayStateContributor[] _contributors;
    private readonly IReadOnlyList<GameplayStateContributor> _contributorsView;

    public ulong StepIndex { get; }
    public string SceneName { get; }
    public ulong Hash { get; }
    public IReadOnlyList<GameplayStateContributor> Contributors => _contributorsView;

    internal GameplayStateSnapshot(
        ulong stepIndex,
        string sceneName,
        ulong hash,
        GameplayStateContributor[] contributors)
    {
        StepIndex = stepIndex;
        SceneName = sceneName;
        Hash = hash;
        _contributors = contributors;
        _contributorsView = Array.AsReadOnly(_contributors);
    }

    internal GameplayStateContributor? FindFirstDifference(GameplayStateSnapshot actual)
    {
        int shared = Math.Min(_contributors.Length, actual._contributors.Length);
        for (int i = 0; i < shared; i++)
        {
            GameplayStateContributor expected = _contributors[i];
            GameplayStateContributor candidate = actual._contributors[i];
            if (expected != candidate) return candidate;
        }
        return actual._contributors.Length > shared ? actual._contributors[shared] : null;
    }

    internal GameplayStateContributor? ExpectedAtFirstDifference(GameplayStateSnapshot actual)
    {
        int shared = Math.Min(_contributors.Length, actual._contributors.Length);
        for (int i = 0; i < shared; i++)
        {
            if (_contributors[i] != actual._contributors[i]) return _contributors[i];
        }
        return _contributors.Length > shared ? _contributors[shared] : null;
    }
}

/// <summary>Versioned immutable state-hash trace used as a replay verification baseline.</summary>
public sealed class GameplayStateRecording
{
    public const int CurrentFormatVersion = 1;
    private readonly GameplayStateSnapshot[] _snapshots;
    private readonly IReadOnlyList<GameplayStateSnapshot> _snapshotsView;

    public int FormatVersion => CurrentFormatVersion;
    public double FixedDeltaSeconds { get; }
    public IReadOnlyList<GameplayStateSnapshot> Snapshots => _snapshotsView;
    public int SnapshotCount => _snapshots.Length;
    public ulong FirstStepIndex => _snapshots.Length == 0 ? 0 : _snapshots[0].StepIndex;
    public ulong LastStepIndex => _snapshots.Length == 0 ? 0 : _snapshots[^1].StepIndex;

    internal GameplayStateRecording(double fixedDeltaSeconds, GameplayStateSnapshot[] snapshots)
    {
        FixedDeltaSeconds = fixedDeltaSeconds;
        _snapshots = snapshots;
        _snapshotsView = Array.AsReadOnly(_snapshots);
    }
}

/// <summary>Collects one state snapshot per committed simulation Step.</summary>
public sealed class GameplayStateRecorder
{
    private readonly List<GameplayStateSnapshot> _snapshots;
    private double? _fixedDeltaSeconds;

    public int SnapshotCount => _snapshots.Count;
    public double? FixedDeltaSeconds => _fixedDeltaSeconds;

    public GameplayStateRecorder(int initialCapacity = 0)
    {
        if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _snapshots = new List<GameplayStateSnapshot>(initialCapacity);
    }

    public void Prepare(double fixedDeltaSeconds)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d)
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        if (_fixedDeltaSeconds is { } existing && existing != fixedDeltaSeconds)
            throw new InvalidOperationException(
                "One GameplayStateRecorder cannot use different fixed delta values.");
        _fixedDeltaSeconds = fixedDeltaSeconds;
    }

    public void Capture(GameplayStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_fixedDeltaSeconds is null)
            throw new InvalidOperationException("Prepare the state recorder before capture.");
        if (_snapshots.Count > 0 && snapshot.StepIndex != _snapshots[^1].StepIndex + 1UL)
            throw new InvalidOperationException("Gameplay state snapshots must use contiguous Steps.");
        _snapshots.Add(snapshot);
    }

    public GameplayStateRecording Snapshot()
    {
        double fixedDelta = _fixedDeltaSeconds ?? throw new InvalidOperationException(
            "Prepare the state recorder before creating a recording.");
        return new GameplayStateRecording(fixedDelta, _snapshots.ToArray());
    }
}

public sealed record GameplayStateDivergence(
    ulong StepIndex,
    ulong ExpectedHash,
    ulong ActualHash,
    GameplayStateContributor? ExpectedContributor,
    GameplayStateContributor? ActualContributor,
    string Reason);

public sealed class GameplayStateDivergenceException(GameplayStateDivergence divergence)
    : InvalidOperationException(
        $"Gameplay state diverged at Step {divergence.StepIndex}: {divergence.Reason} " +
        $"(expected 0x{divergence.ExpectedHash:X16}, actual 0x{divergence.ActualHash:X16}).")
{
    public GameplayStateDivergence Divergence { get; } = divergence;
}

/// <summary>Compares live snapshots to a baseline and retains only the first divergence.</summary>
public sealed class GameplayStateVerifier
{
    private readonly GameplayStateRecording _recording;
    private int _nextSnapshot;

    public ulong CurrentStepIndex { get; private set; }
    public bool IsComplete => _nextSnapshot == _recording.SnapshotCount;
    public GameplayStateDivergence? FirstDivergence { get; private set; }
    public GameplayStateRecording Recording => _recording;

    public GameplayStateVerifier(GameplayStateRecording recording) =>
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));

    public bool Verify(GameplayStateSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (FirstDivergence is not null) return false;
        CurrentStepIndex = actual.StepIndex;
        if (_nextSnapshot >= _recording.SnapshotCount)
        {
            FirstDivergence = new GameplayStateDivergence(
                actual.StepIndex, 0, actual.Hash, null,
                actual.Contributors.Count > 0 ? actual.Contributors[0] : null,
                "The baseline has no snapshot for this Step.");
            return false;
        }

        GameplayStateSnapshot expected = _recording.Snapshots[_nextSnapshot];
        _nextSnapshot++;
        if (expected.StepIndex == actual.StepIndex && expected.Hash == actual.Hash) return true;

        string reason = expected.StepIndex != actual.StepIndex
            ? $"Expected Step {expected.StepIndex}, but received Step {actual.StepIndex}."
            : "The first differing state contributor is shown in the diagnostic.";
        FirstDivergence = new GameplayStateDivergence(
            actual.StepIndex,
            expected.Hash,
            actual.Hash,
            expected.ExpectedAtFirstDifference(actual),
            expected.FindFirstDifference(actual),
            reason);
        return false;
    }
}
