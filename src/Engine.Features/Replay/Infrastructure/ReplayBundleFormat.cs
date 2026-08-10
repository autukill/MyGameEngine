namespace GameEngine.Features.Replay.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Replay.Domain;

public static class ReplayBundleWriter
{
    private static readonly byte[] Magic = "MGRP"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int ContainerOverhead = 4 + sizeof(int) + sizeof(long) + 32;

    public static void Write(
        Stream destination,
        ReplayBundle bundle,
        ReplayBundleLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(bundle);
        if (!destination.CanWrite)
            throw new ArgumentException("Replay destination must be writable.", nameof(destination));

        limits ??= new ReplayBundleLimits();
        limits.Validate();
        ValidateWithinLimits(bundle, limits);

        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Utf8, leaveOpen: true))
            WritePayload(writer, bundle);

        long totalLength = checked(ContainerOverhead + payload.Length);
        if (totalLength > limits.MaxFileBytes)
        {
            throw new InvalidOperationException(
                $"Replay bundle requires {totalLength} bytes, exceeding the configured " +
                $"limit of {limits.MaxFileBytes} bytes.");
        }

        if (!payload.TryGetBuffer(out ArraySegment<byte> bytes))
            throw new InvalidOperationException("Replay payload buffer is unavailable.");
        byte[] checksum = SHA256.HashData(bytes.AsSpan(0, checked((int)payload.Length)));

        using var output = new BinaryWriter(destination, Utf8, leaveOpen: true);
        output.Write(Magic);
        output.Write(ReplayBundle.CurrentFormatVersion);
        output.Write(payload.Length);
        output.Write(bytes.Array!, bytes.Offset, checked((int)payload.Length));
        output.Write(checksum);
        output.Flush();
    }

    public static void Write(
        string path,
        ReplayBundle bundle,
        ReplayBundleLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bundle);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Replay directory does not exist: '{directory}'.");

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                Write(file, bundle, limits);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception cleanupFailure)
                when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                // Preserve the original save/move failure; the uniquely named temp is recoverable.
            }
        }
    }

    private static void WritePayload(BinaryWriter writer, ReplayBundle bundle)
    {
        WriteString(writer, bundle.Identity.GameId);
        WriteString(writer, bundle.Identity.BuildId);
        writer.Write(LogicalInputRecording.CurrentFormatVersion);
        writer.Write(GameplayStateRecording.CurrentFormatVersion);
        writer.Write(GameplayStateWriter.AlgorithmVersion);
        writer.Write(BitConverter.DoubleToInt64Bits(bundle.FixedDeltaSeconds));

        ReadOnlySpan<InputActionRef> actions = bundle.Input.Actions.Span;
        writer.Write(actions.Length);
        for (int i = 0; i < actions.Length; i++) WriteString(writer, actions[i].Name);

        ReadOnlySpan<InputAxis2DRef> axes = bundle.Input.Axes2D.Span;
        writer.Write(axes.Length);
        for (int i = 0; i < axes.Length; i++) WriteString(writer, axes[i].Name);

        writer.Write(bundle.Input.FrameCount);
        foreach (LogicalInputFrame frame in bundle.Input.Frames)
        {
            writer.Write(frame.StepIndex);
            for (int i = 0; i < frame.ActionCount; i++)
                writer.Write((byte)frame.GetActionState(i));
            for (int i = 0; i < frame.Axis2DCount; i++)
            {
                Vector2D value = frame.GetAxis2D(i);
                writer.Write(BitConverter.SingleToInt32Bits(value.X));
                writer.Write(BitConverter.SingleToInt32Bits(value.Y));
            }
        }

        writer.Write(bundle.GameplayState.SnapshotCount);
        foreach (GameplayStateSnapshot snapshot in bundle.GameplayState.Snapshots)
        {
            writer.Write(snapshot.StepIndex);
            WriteString(writer, snapshot.SceneName);
            writer.Write(snapshot.Hash);
            writer.Write(snapshot.Contributors.Count);
            foreach (GameplayStateContributor contributor in snapshot.Contributors)
            {
                writer.Write(contributor.Sequence);
                WriteString(writer, contributor.Kind);
                writer.Write(contributor.Hash);
            }
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void ValidateWithinLimits(ReplayBundle bundle, ReplayBundleLimits limits)
    {
        if (bundle.FrameCount > limits.MaxFrames)
            throw new InvalidOperationException("Replay frame count exceeds the configured limit.");
        if (bundle.Input.Actions.Length > limits.MaxActions)
            throw new InvalidOperationException("Replay action count exceeds the configured limit.");
        if (bundle.Input.Axes2D.Length > limits.MaxAxes2D)
            throw new InvalidOperationException("Replay axis count exceeds the configured limit.");
        ValidateString(bundle.Identity.GameId, limits);
        ValidateString(bundle.Identity.BuildId, limits);
        foreach (GameplayStateSnapshot snapshot in bundle.GameplayState.Snapshots)
        {
            if (snapshot.Contributors.Count > limits.MaxContributorsPerFrame)
                throw new InvalidOperationException(
                    "Replay contributor count exceeds the configured limit.");
            ValidateString(snapshot.SceneName, limits);
            foreach (GameplayStateContributor contributor in snapshot.Contributors)
                ValidateString(contributor.Kind, limits);
        }
        foreach (InputActionRef action in bundle.Input.Actions.Span)
            ValidateString(action.Name, limits);
        foreach (InputAxis2DRef axis in bundle.Input.Axes2D.Span)
            ValidateString(axis.Name, limits);
    }

    private static void ValidateString(string value, ReplayBundleLimits limits)
    {
        if (Utf8.GetByteCount(value) > limits.MaxStringBytes)
            throw new InvalidOperationException("Replay string exceeds the configured byte limit.");
    }
}

public static class ReplayBundleReader
{
    private static readonly byte[] Magic = "MGRP"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const int ContainerOverhead = 4 + sizeof(int) + sizeof(long) + 32;
    private const LogicalInputActionState ValidActionStates =
        LogicalInputActionState.Down |
        LogicalInputActionState.Pressed |
        LogicalInputActionState.Released;

    public static ReplayBundle Read(Stream source, ReplayBundleLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Replay source must be readable.", nameof(source));
        limits ??= new ReplayBundleLimits();
        limits.Validate();
        if (source.CanSeek && source.Length - source.Position > limits.MaxFileBytes)
            throw new InvalidDataException("Replay file exceeds the configured byte limit.");

        try
        {
            using var reader = new BinaryReader(source, Utf8, leaveOpen: true);
            byte[] magic = reader.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("Replay file magic is invalid.");
            int version = reader.ReadInt32();
            if (version != ReplayBundle.CurrentFormatVersion)
                throw new InvalidDataException($"Unsupported replay format version {version}.");

            long payloadLength = reader.ReadInt64();
            long maximumPayload = limits.MaxFileBytes - ContainerOverhead;
            if (payloadLength < 0 || payloadLength > maximumPayload || payloadLength > int.MaxValue)
                throw new InvalidDataException("Replay payload length is invalid or exceeds limits.");

            byte[] payload = reader.ReadBytes(checked((int)payloadLength));
            if (payload.Length != payloadLength)
                throw new InvalidDataException("Replay payload is truncated.");
            byte[] expectedChecksum = reader.ReadBytes(32);
            if (expectedChecksum.Length != 32)
                throw new InvalidDataException("Replay checksum is truncated.");
            byte[] actualChecksum = SHA256.HashData(payload);
            if (!CryptographicOperations.FixedTimeEquals(expectedChecksum, actualChecksum))
                throw new InvalidDataException("Replay payload checksum does not match.");
            if (source.ReadByte() != -1)
                throw new InvalidDataException("Replay file contains trailing data.");

            return ReadPayload(payload, limits);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Replay file is truncated.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Replay file contains invalid UTF-8.", exception);
        }
    }

    public static ReplayBundle Read(string path, ReplayBundleLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, limits);
    }

    private static ReplayBundle ReadPayload(byte[] payload, ReplayBundleLimits limits)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Utf8, leaveOpen: false);

        var identity = new ReplayIdentity(
            ReadRequiredString(reader, limits, "game ID"),
            ReadRequiredString(reader, limits, "build ID"));
        RequireVersion(reader.ReadInt32(), LogicalInputRecording.CurrentFormatVersion, "input");
        RequireVersion(reader.ReadInt32(), GameplayStateRecording.CurrentFormatVersion, "state");
        RequireVersion(reader.ReadInt32(), GameplayStateWriter.AlgorithmVersion, "state hash");
        double fixedDelta = BitConverter.Int64BitsToDouble(reader.ReadInt64());
        if (!double.IsFinite(fixedDelta) || fixedDelta <= 0d)
            throw new InvalidDataException("Replay fixed delta is invalid.");

        int actionCount = ReadCount(reader, limits.MaxActions, "action");
        var actions = new InputActionRef[actionCount];
        var actionNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < actionCount; i++)
        {
            string name = ReadRequiredString(reader, limits, "action name");
            if (!actionNames.Add(name))
                throw new InvalidDataException($"Replay contains duplicate action '{name}'.");
            actions[i] = new InputActionRef(name);
        }

        int axisCount = ReadCount(reader, limits.MaxAxes2D, "axis");
        var axes = new InputAxis2DRef[axisCount];
        var axisNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < axisCount; i++)
        {
            string name = ReadRequiredString(reader, limits, "axis name");
            if (!axisNames.Add(name))
                throw new InvalidDataException($"Replay contains duplicate axis '{name}'.");
            axes[i] = new InputAxis2DRef(name);
        }

        int frameCount = ReadCount(reader, limits.MaxFrames, "frame", allowZero: false);
        var frames = new LogicalInputFrame[frameCount];
        var actionStates = new LogicalInputActionState[actionCount];
        var axisValues = new Vector2D[axisCount];
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            ulong stepIndex = reader.ReadUInt64();
            if (stepIndex != (ulong)frameIndex + 1UL)
                throw new InvalidDataException("Replay input Tick indices are not contiguous.");
            for (int i = 0; i < actionCount; i++)
            {
                var actionState = (LogicalInputActionState)reader.ReadByte();
                if ((actionState & ~ValidActionStates) != 0)
                    throw new InvalidDataException("Replay contains an invalid action state.");
                actionStates[i] = actionState;
            }
            for (int i = 0; i < axisCount; i++)
            {
                float x = BitConverter.Int32BitsToSingle(reader.ReadInt32());
                float y = BitConverter.Int32BitsToSingle(reader.ReadInt32());
                if (!float.IsFinite(x) || !float.IsFinite(y))
                    throw new InvalidDataException("Replay contains a non-finite axis value.");
                axisValues[i] = new Vector2D(x, y);
            }
            frames[frameIndex] = new LogicalInputFrame(stepIndex, actionStates, axisValues);
        }

        int snapshotCount = ReadCount(reader, limits.MaxFrames, "state snapshot", allowZero: false);
        if (snapshotCount != frameCount)
            throw new InvalidDataException("Replay input and state frame counts differ.");
        var snapshots = new GameplayStateSnapshot[snapshotCount];
        for (int snapshotIndex = 0; snapshotIndex < snapshotCount; snapshotIndex++)
        {
            ulong stepIndex = reader.ReadUInt64();
            if (stepIndex != (ulong)snapshotIndex + 1UL)
                throw new InvalidDataException("Replay state Tick indices are not contiguous.");
            string sceneName = ReadRequiredString(reader, limits, "Scene name");
            ulong hash = reader.ReadUInt64();
            int contributorCount = ReadCount(
                reader, limits.MaxContributorsPerFrame, "state contributor");
            var contributors = new GameplayStateContributor[contributorCount];
            for (int i = 0; i < contributorCount; i++)
            {
                long sequence = reader.ReadInt64();
                string kind = ReadRequiredString(reader, limits, "contributor kind");
                contributors[i] = new GameplayStateContributor(sequence, kind, reader.ReadUInt64());
            }
            snapshots[snapshotIndex] = new GameplayStateSnapshot(
                stepIndex, sceneName, hash, contributors);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Replay payload contains trailing data.");

        var input = new LogicalInputRecording(actions, axes, frames, fixedDelta);
        var stateRecording = new GameplayStateRecording(fixedDelta, snapshots);
        return new ReplayBundle(identity, input, stateRecording);
    }

    private static int ReadCount(
        BinaryReader reader,
        int maximum,
        string description,
        bool allowZero = true)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum || (!allowZero && value == 0))
            throw new InvalidDataException($"Replay {description} count is invalid or exceeds limits.");
        return value;
    }

    private static string ReadString(BinaryReader reader, ReplayBundleLimits limits)
    {
        int byteCount = reader.ReadInt32();
        if (byteCount < 0 || byteCount > limits.MaxStringBytes)
            throw new InvalidDataException("Replay string length is invalid or exceeds limits.");
        byte[] bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
            throw new EndOfStreamException();
        return Utf8.GetString(bytes);
    }

    private static string ReadRequiredString(
        BinaryReader reader,
        ReplayBundleLimits limits,
        string description)
    {
        string value = ReadString(reader, limits);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Replay {description} cannot be empty.");
        return value;
    }

    private static void RequireVersion(int actual, int expected, string component)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Replay {component} version {actual} is incompatible with version {expected}.");
        }
    }
}
