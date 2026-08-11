namespace WorldStreaming.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Features.WorldStreaming;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("Chunk layout and exact boundaries", LayoutBehavior);
        Run("Visible, preload, and retained residency", ResidencyBehavior);
        Run("Concurrency and cancellation", ConcurrencyAndCancellation);
        Run("Synchronous load start pacing", SynchronousLoadPacing);
        Run("Failure retry on Viewport revision", FailureRetry);
        Run("Observer failures preserve loaded state", ObserverFailureIsolation);
        Run("Tracked budget is atomic", TrackedBudget);
        Run("Lease lifecycle and idempotent Dispose", LeaseLifecycle);
        Run("Stable snapshot allocation", StableAllocation);

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All WorldStreaming tests passed ==="
            : $"=== {_failures} WorldStreaming test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void LayoutBehavior()
    {
        var layout = new WorldChunkLayout(new Vector2(256f));
        WorldChunkRange exact = layout.GetRange(new Bounds2D(0f, 0f, 512f, 512f));
        Check(exact == new WorldChunkRange(0, 0, 1, 1) && exact.Count == 4,
            "An exact right/bottom boundary does not include the next chunk");
        WorldChunkRange negative = layout.GetRange(new Bounds2D(-1f, -257f, 1f, -1f));
        Check(negative == new WorldChunkRange(-1, -2, 0, -1),
            "Negative world coordinates use mathematical floor");
        Bounds2D bounds = layout.GetBounds(new WorldChunkCoordinate(-1, 2));
        Check(bounds == new Bounds2D(-256f, 512f, 0f, 768f),
            "Chunk coordinates map back to deterministic world bounds");
    }

    private static void ResidencyBehavior()
    {
        var loader = new ImmediateLoader();
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            loader,
            new WorldChunkStreamingOptions(1, 2, 4, 100, true, 32));
        WorldChunkUpdateResult first = streamer.Update(Snapshot(0f, 0f, 512f, 512f, 1));
        WorldChunkStreamingDiagnostics diagnostics = streamer.CaptureDiagnostics();
        Check(first.DesiredSetChanged && first.LoadsStarted == 16,
            "Visible chunks load before the surrounding preload ring");
        Check(diagnostics.TrackedCount == 36 && diagnostics.LoadedCount == 16 &&
              diagnostics.PendingCount == 20 && diagnostics.VisibleCount == 4 &&
              diagnostics.PreloadedCount == 12 && diagnostics.RetainedCount == 20,
            "Visible, preload, and retained rings remain distinct");
        Check(streamer.TryGetChunk(new WorldChunkCoordinate(0, 0), out TestLease? visible) &&
              visible is not null &&
              streamer.GetResidency(new WorldChunkCoordinate(0, 0)) == WorldChunkResidency.Visible,
            "Loaded chunks are addressable without exposing loader internals");

        WorldChunkUpdateResult moved = streamer.Update(Snapshot(1_024f, 0f, 1_536f, 512f, 2));
        Check(moved.DesiredSetChanged && moved.ChunksUnloaded > 0,
            "Moving beyond the retained ring unloads old leases");
        Check(loader.DisposedCount == moved.ChunksUnloaded,
            "Every unloaded chunk disposes exactly one lease");
    }

    private static void ConcurrencyAndCancellation()
    {
        var loader = new ControlledLoader();
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            loader,
            new WorldChunkStreamingOptions(0, 0, 2, 32));
        WorldChunkUpdateResult first = streamer.Update(Snapshot(0f, 0f, 512f, 512f, 1));
        Check(first.LoadsStarted == 2 && streamer.ActiveLoadCount == 2,
            "Concurrency budget starts only two of four visible loads");
        loader.Complete(new WorldChunkCoordinate(0, 0));
        loader.Complete(new WorldChunkCoordinate(1, 0));
        WorldChunkUpdateResult second = streamer.Update(Snapshot(0f, 0f, 512f, 512f, 1));
        Check(second.LoadsCompleted == 2 && second.LoadsStarted == 2 &&
              streamer.ActiveLoadCount == 2,
            "Completed slots immediately admit the next visible loads");

        streamer.Update(Snapshot(2_048f, 0f, 2_560f, 512f, 2));
        Check(loader.CancelledCount == 2,
            "Leaving the retained set cancels in-flight chunk loads");
        WorldChunkUpdateResult afterCancel =
            streamer.Update(Snapshot(2_048f, 0f, 2_560f, 512f, 2));
        WorldChunkUpdateResult nextAfterCancel =
            streamer.Update(Snapshot(2_048f, 0f, 2_560f, 512f, 2));
        int replacementStarts = afterCancel.LoadsStarted + nextAfterCancel.LoadsStarted;
        Check(replacementStarts == 2 && streamer.ActiveLoadCount == 2,
            $"Cancelled slots admit new visible work as completions are observed " +
            $"(started={replacementStarts}, active={streamer.ActiveLoadCount})");
    }

    private static void FailureRetry()
    {
        var loader = new FailOnceLoader();
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            loader,
            new WorldChunkStreamingOptions(0, 0, 1, 8, true));
        int failures = 0;
        streamer.ChunkFailed += _ => failures++;
        ViewportSnapshot first = Snapshot(0f, 0f, 256f, 256f, 10);
        streamer.Update(first);
        streamer.Update(first);
        Check(failures == 1 && streamer.CaptureDiagnostics().FailedCount == 1,
            "A failed load is observed once and not retried every frame");
        streamer.Update(Snapshot(0f, 0f, 256f, 256f, 11));
        Check(streamer.TryGetChunk(new WorldChunkCoordinate(0, 0), out _),
            "A new Viewport revision retries a failed desired chunk");
    }

    private static void SynchronousLoadPacing()
    {
        var loader = new ImmediateLoader();
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            loader,
            new WorldChunkStreamingOptions(0, 0, 4, 16, true, 2));
        ViewportSnapshot snapshot = Snapshot(0f, 0f, 512f, 512f, 1);
        WorldChunkUpdateResult first = streamer.Update(snapshot);
        WorldChunkUpdateResult second = streamer.Update(snapshot);
        Check(first.LoadsStarted == 2 && second.LoadsStarted == 2 && loader.CreatedCount == 4,
            "Per-update pacing also limits synchronous cache hits");
    }

    private static void TrackedBudget()
    {
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            new ImmediateLoader(),
            new WorldChunkStreamingOptions(1, 2, 4, 10));
        Throws<InvalidOperationException>(() =>
            streamer.Update(Snapshot(0f, 0f, 1_024f, 1_024f, 1)));
        Check(streamer.TrackedCount == 0 && streamer.LastViewportRevision == 0,
            "An oversized desired set fails before mutating streamer state");
    }

    private static void ObserverFailureIsolation()
    {
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            new ImmediateLoader(),
            new WorldChunkStreamingOptions(0, 0, 1, 8));
        streamer.ChunkLoaded += _ => throw new InvalidOperationException("observer");
        Throws<InvalidOperationException>(() =>
            streamer.Update(Snapshot(0f, 0f, 256f, 256f, 1)));
        Check(streamer.TryGetChunk(new WorldChunkCoordinate(0, 0), out _) &&
              streamer.CaptureDiagnostics().FailedCount == 0,
            "An observer exception does not rewrite successful loader state");
    }

    private static void LeaseLifecycle()
    {
        var loader = new ImmediateLoader();
        var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            loader,
            new WorldChunkStreamingOptions(0, 0, 4, 16));
        streamer.Update(Snapshot(0f, 0f, 512f, 512f, 1));
        Check(loader.CreatedCount == 4 && loader.DisposedCount == 0,
            "Loaded leases remain owned while retained");
        streamer.Dispose();
        streamer.Dispose();
        Check(loader.DisposedCount == 4,
            "Dispose is idempotent and releases every remaining lease exactly once");
    }

    private static void StableAllocation()
    {
        using var streamer = new WorldChunkStreamer<TestLease>(
            new WorldChunkLayout(new Vector2(256f)),
            new ImmediateLoader(),
            new WorldChunkStreamingOptions(0, 0, 4, 16));
        ViewportSnapshot snapshot = Snapshot(0f, 0f, 512f, 512f, 1);
        streamer.Update(snapshot);
        for (int i = 0; i < 256; i++) streamer.Update(snapshot);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) streamer.Update(snapshot);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0,
            $"An unchanged fully loaded Snapshot allocates 0 B, actual {allocated:N0} B");
    }

    private static ViewportSnapshot Snapshot(
        float left,
        float top,
        float right,
        float bottom,
        ulong revision)
    {
        var bounds = new Bounds2D(left, top, right, bottom);
        return new ViewportSnapshot(
            bounds,
            new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f),
            1f,
            new Vector2(right - left, bottom - top),
            revision);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception exception)
        {
            _failures++;
            Console.WriteLine($"[FAIL] {name}: {exception.Message}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class TestLease(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }

    private sealed class ImmediateLoader : IWorldChunkLoader<TestLease>
    {
        public int CreatedCount { get; private set; }
        public int DisposedCount { get; private set; }

        public ValueTask<TestLease> LoadAsync(
            WorldChunkCoordinate coordinate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreatedCount++;
            return ValueTask.FromResult(new TestLease(() => DisposedCount++));
        }
    }

    private sealed class ControlledLoader : IWorldChunkLoader<TestLease>
    {
        private readonly Dictionary<WorldChunkCoordinate, Pending> _pending = [];
        public int CancelledCount { get; private set; }

        public ValueTask<TestLease> LoadAsync(
            WorldChunkCoordinate coordinate,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<TestLease>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                CancelledCount++;
                completion.TrySetCanceled(cancellationToken);
            });
            _pending.Add(coordinate, new Pending(completion, registration));
            return new ValueTask<TestLease>(completion.Task);
        }

        public void Complete(WorldChunkCoordinate coordinate)
        {
            Pending pending = _pending[coordinate];
            pending.Registration.Dispose();
            pending.Completion.SetResult(new TestLease(() => { }));
            _pending.Remove(coordinate);
        }

        private readonly record struct Pending(
            TaskCompletionSource<TestLease> Completion,
            CancellationTokenRegistration Registration);
    }

    private sealed class FailOnceLoader : IWorldChunkLoader<TestLease>
    {
        private bool _failed;

        public ValueTask<TestLease> LoadAsync(
            WorldChunkCoordinate coordinate,
            CancellationToken cancellationToken)
        {
            if (!_failed)
            {
                _failed = true;
                return ValueTask.FromException<TestLease>(new IOException("simulated"));
            }
            return ValueTask.FromResult(new TestLease(() => { }));
        }
    }
}
