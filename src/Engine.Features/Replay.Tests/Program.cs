namespace Replay.Tests;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Replay.Application;
using GameEngine.Features.Replay.Domain;
using GameEngine.Features.Replay.Infrastructure;

internal static class Program
{
    private static readonly InputActionRef Fire = new("fire");
    private static readonly InputAxis2DRef Move = new("move");
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Replay Feature Smoke Test ===\n");
        TestDeterministicRoundTrip();
        TestIntegrityAndVersionValidation();
        TestLimitsAndIdentity();
        TestReplaySessionLifecycle();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Replay smoke tests passed ==="
            : $"=== {_failures} Replay test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestDeterministicRoundTrip()
    {
        Console.WriteLine("1. Deterministic binary round trip");
        ReplayBundle original = CreateBundle();
        byte[] first = Write(original);
        byte[] second = Write(original);
        Check(first.AsSpan().SequenceEqual(second),
            "The same bundle produces byte-for-byte identical output");
        Check(first.AsSpan(0, 4).SequenceEqual("MGRP"u8),
            "The container starts with the stable MGRP magic");

        ReplayBundle loaded = ReplayBundleReader.Read(new MemoryStream(first));
        Check(loaded.Identity == original.Identity &&
              loaded.FrameCount == 2 &&
              BitConverter.DoubleToInt64Bits(loaded.FixedDeltaSeconds) ==
              BitConverter.DoubleToInt64Bits(1d / 60d),
            "Identity, Tick range, and fixed delta survive round trip");
        Check(loaded.Input.Actions.Span.SequenceEqual(original.Input.Actions.Span) &&
              loaded.Input.Axes2D.Span.SequenceEqual(original.Input.Axes2D.Span),
            "Logical input schema survives round trip");
        Check(loaded.Input.Frames[0].GetActionState(0) ==
              (LogicalInputActionState.Down | LogicalInputActionState.Pressed) &&
              loaded.Input.Frames[1].GetAxis2D(0) == new Vector2D(-1f, -1f),
            "Action edge bits and exact axis values survive round trip");
        Check(loaded.GameplayState.Snapshots[0].Hash ==
              original.GameplayState.Snapshots[0].Hash &&
              loaded.GameplayState.Snapshots[1].Contributors.Count ==
              original.GameplayState.Snapshots[1].Contributors.Count,
            "State hashes and contributor diagnostics survive round trip");
    }

    private static void TestIntegrityAndVersionValidation()
    {
        Console.WriteLine("2. Integrity and version validation");
        byte[] corrupted = Write(CreateBundle());
        corrupted[24] ^= 0x40;
        CheckThrows<InvalidDataException>(
            () => ReplayBundleReader.Read(new MemoryStream(corrupted)),
            "A corrupted payload is rejected by SHA-256");

        byte[] futureVersion = Write(CreateBundle());
        futureVersion[4] = 2;
        CheckThrows<InvalidDataException>(
            () => ReplayBundleReader.Read(new MemoryStream(futureVersion)),
            "An unknown container version is rejected before parsing");

        byte[] truncated = Write(CreateBundle())[..^8];
        CheckThrows<InvalidDataException>(
            () => ReplayBundleReader.Read(new MemoryStream(truncated)),
            "A truncated checksum is rejected with a data error");
    }

    private static void TestLimitsAndIdentity()
    {
        Console.WriteLine("3. Limits and caller identity");
        ReplayBundle bundle = CreateBundle();
        CheckThrows<InvalidOperationException>(
            () => ReplayBundleWriter.Write(
                new MemoryStream(),
                bundle,
                new ReplayBundleLimits(MaxFrames: 1)),
            "Writer limits fail before emitting an oversized Tick stream");
        byte[] bytes = Write(bundle);
        CheckThrows<InvalidDataException>(
            () => ReplayBundleReader.Read(
                new MemoryStream(bytes),
                new ReplayBundleLimits(MaxFrames: 1)),
            "Reader limits reject an untrusted oversized Tick stream");
        CheckThrows<InvalidOperationException>(
            () => bundle.ValidateIdentity(new ReplayIdentity("another-game", "dev")),
            "A replay from another game is rejected");
        CheckThrows<InvalidOperationException>(
            () => ReplaySession.Play(bundle, new ReplayIdentity("asteroids", "release")),
            "A replay from another build is rejected");
    }

    private static void TestReplaySessionLifecycle()
    {
        Console.WriteLine("4. Replay session lifecycle");
        var identity = new ReplayIdentity("asteroids", "dev");
        ReplaySession recording = ReplaySession.Record(identity, initialFrameCapacity: 2);
        Populate(recording.InputRecorder!, recording.StateRecorder!);
        using var stream = new MemoryStream();
        recording.Save(stream);
        Check(stream.Length > 64 && recording.Snapshot().FrameCount == 2,
            "A recording session snapshots and saves both streams together");

        stream.Position = 0;
        ReplaySession playback = ReplaySession.Load(stream, identity);
        Check(playback.Mode == ReplaySessionMode.Playback &&
              playback.Bundle is { FrameCount: 2 } &&
              playback.InputRecorder is null &&
              playback.StateRecorder is null,
            "A loaded session is immutable playback state");
        CheckThrows<InvalidOperationException>(
            () => playback.Snapshot(),
            "Playback sessions cannot be accidentally overwritten as recordings");
    }

    private static ReplayBundle CreateBundle()
    {
        var input = new LogicalInputRecorder(2);
        var state = new GameplayStateRecorder(2);
        Populate(input, state);
        return new ReplayBundle(
            new ReplayIdentity("asteroids", "dev"),
            input.Snapshot(),
            state.Snapshot());
    }

    private static void Populate(LogicalInputRecorder input, GameplayStateRecorder state)
    {
        const double fixedDelta = 1d / 60d;
        InputMap map = new InputMapBuilder()
            .BindAction(Fire, InputKey.Space)
            .BindAxis2D(Move, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
            .Build();
        var physical = new TestInputProvider();
        input.Prepare(map, fixedDelta);
        state.Prepare(fixedDelta);
        var scene = new SceneAggregate(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Main");

        physical.Step = 1;
        input.BeginStep(1, map, physical);
        scene.PerformStep(fixedDelta);
        state.Capture(scene.CaptureGameplayState());

        physical.Step = 2;
        input.BeginStep(2, map, physical);
        scene.PerformStep(fixedDelta);
        state.Capture(scene.CaptureGameplayState());
    }

    private static byte[] Write(ReplayBundle bundle)
    {
        using var stream = new MemoryStream();
        ReplayBundleWriter.Write(stream, bundle);
        return stream.ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {message}");
            return;
        }
        _failures++;
        Console.WriteLine($"  [FAIL] {message}");
    }

    private static void CheckThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
            Check(false, message);
        }
        catch (TException)
        {
            Check(true, message);
        }
    }

    private sealed class TestInputProvider : IInputProvider
    {
        public int Step { get; set; }
        public Vector2D MousePosition => Vector2D.Zero;
        public float MouseScrollDelta => 0f;
        public bool IsKeyDown(InputKey key) => Step switch
        {
            1 => key is InputKey.Space or InputKey.D,
            2 => key is InputKey.A or InputKey.W,
            _ => false
        };
        public bool WasKeyPressed(InputKey key) => Step == 1 && key == InputKey.Space;
        public bool WasKeyReleased(InputKey key) => Step == 2 && key == InputKey.Space;
        public bool IsMouseButtonDown(MouseButton button) => false;
    }
}
