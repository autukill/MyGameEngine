namespace Animation.Tests;

using GameEngine.Features.Animation;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Animation Authoring Smoke Test ===\n");
        VerifyRegistration();
        VerifyLoopingAndEvents();
        VerifyOnceAndReverse();
        VerifyPingPong();
        VerifyAllocationBoundary();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Animation smoke tests passed ==="
            : $"=== {_failures} Animation test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyRegistration()
    {
        Console.WriteLine("1. Immutable named clips");
        var library = new AnimationLibrary();
        int[] callerFrames = [2, 4, 6];
        AnimationClipRef clipRef = library.Register("player.run", callerFrames, 12f);
        callerFrames[0] = 99;
        AnimationClip clip = library.Get(clipRef);

        Check(clip.FrameCount == 3 && clip.GetSubImage(0) == 2, "Registration freezes caller-owned frames");
        Check(clip.FramesPerSecond == 12f && clip.LoopMode == AnimationLoopMode.Loop,
            "Clip retains FPS and loop mode");
        CheckThrows<ArgumentException>(() => library.Register("player.run", [0], 1f),
            "Duplicate names are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => library.Register("bad.fps", [0], 0f),
            "Non-positive FPS is rejected");
        CheckThrows<ArgumentException>(() => library.Register("empty", [], 1f),
            "Empty clips are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => library.Register("bad.frame", [-1], 1f),
            "Negative sub-images are rejected");
    }

    private static void VerifyLoopingAndEvents()
    {
        Console.WriteLine("2. Loop playback and frame events");
        var library = new AnimationLibrary();
        AnimationClipRef clip = library.Register(
            "player.attack",
            [10, 11, 12],
            10f,
            AnimationLoopMode.Loop,
            [new AnimationFrameMarker(1, new AnimationEventRef("attack.hit"))]);
        var player = new AnimationPlayer(library);
        var events = new AnimationEventBuffer();
        player.Play(clip);

        AnimationUpdateResult first = player.Update(0.1, events);
        Check(first.CurrentSubImage == 11 && events.Count == 1 &&
              events.Items[0].Event == new AnimationEventRef("attack.hit"),
            "Entering a marked frame emits one typed event");

        AnimationUpdateResult wrapped = player.Update(0.2, events);
        Check(wrapped.CurrentSubImage == 10 && wrapped.CompletedCycles == 1 && player.CompletedCycles == 1,
            "Large updates advance deterministically and report loop completion");
        Check(events.Count == 0, "Event buffer is cleared for every update");
    }

    private static void VerifyOnceAndReverse()
    {
        Console.WriteLine("3. Once completion and reverse playback");
        var library = new AnimationLibrary();
        AnimationClipRef once = library.Register("explosion", [5, 6, 7], 4f, AnimationLoopMode.Once);
        var player = new AnimationPlayer(library);
        player.Play(once);
        AnimationUpdateResult result = player.Update(1.0);
        Check(result.CurrentSubImage == 7 && result.JustCompleted && player.IsComplete && !player.IsPlaying,
            "Once clips clamp to their terminal frame and complete once");
        Check(!player.Update(1.0).JustCompleted, "Completed clips do not repeat completion edges");

        player.Play(once, restart: true, speed: -1f);
        Check(player.CurrentSubImage == 7, "Reverse playback starts at the last frame");
        result = player.Update(0.5);
        Check(result.CurrentSubImage == 5 && result.JustCompleted,
            "Reverse once playback completes at the first frame");
    }

    private static void VerifyPingPong()
    {
        Console.WriteLine("4. Ping-pong playback");
        var library = new AnimationLibrary();
        AnimationClipRef ping = library.Register("hover", [0, 1, 2], 2f, AnimationLoopMode.PingPong);
        var player = new AnimationPlayer(library);
        player.Play(ping);

        Check(player.Update(0.5).CurrentSubImage == 1, "Ping-pong advances toward the far edge");
        Check(player.Update(0.5).CurrentSubImage == 2, "Ping-pong reaches the far edge");
        Check(player.Update(0.5).CurrentSubImage == 1, "Ping-pong reverses without duplicating the edge frame");
        AnimationUpdateResult cycle = player.Update(0.5);
        Check(cycle.CurrentSubImage == 0 && cycle.CompletedCycles == 1,
            "Ping-pong reports a cycle when it returns to the start edge");

        player.SetSpeed(-2f);
        Check(player.Update(0.25).CurrentSubImage == 1,
            "Changing speed sign reverses the active direction");
    }

    private static void VerifyAllocationBoundary()
    {
        Console.WriteLine("5. Warmed update allocation boundary");
        var library = new AnimationLibrary();
        AnimationClipRef clip = library.Register(
            "steady",
            [0, 1],
            60f,
            markers: [new AnimationFrameMarker(1, new AnimationEventRef("tick"))]);
        var player = new AnimationPlayer(library);
        var events = new AnimationEventBuffer(2);
        player.Play(clip);
        for (var i = 0; i < 32; i++)
            player.Update(1d / 60d, events);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
            player.Update(1d / 60d, events);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Check(allocated == 0, $"Animation updates remain allocation-free after warmup ({allocated} B)");
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        _failures++;
        Console.WriteLine($"  [FAIL] {name}");
    }

    private static void CheckThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (TException)
        {
            Check(true, name);
        }
    }
}
