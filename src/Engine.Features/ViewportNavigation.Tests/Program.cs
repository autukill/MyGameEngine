namespace ViewportNavigation.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ViewportNavigation;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Run("Viewport geometry and revision", GeometryAndRevision);
        Run("Unified pointer provider compatibility", PointerProviderCompatibility);
        Run("Plugin manager order and lifecycle", PluginManagerLifecycle);
        Run("Drag and frame-rate-independent deceleration", DragAndDeceleration);
        Run("Drag threshold and axis", DragThresholdAndAxis);
        Run("Unified multi-pointer pinch and drag handoff", PinchBehavior);
        Run("Wheel anchor, smoothing, and reverse", WheelBehavior);
        Run("ClampZoom scale and visible-size constraints", ClampZoomBehavior);
        Run("Clamp bounds and underflow", ClampBehavior);
        Run("Declarative configuration", DeclarativeConfiguration);
        Run("Validation", Validation);
        Run("Stable update allocation", StableUpdateAllocation);

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All ViewportNavigation tests passed ==="
            : $"=== {_failures} ViewportNavigation test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void PointerProviderCompatibility()
    {
        IInputProvider provider = new MouseOnlyInputProvider();
        Check(provider.PointerCount == 1, "Legacy mouse providers expose one unified pointer");
        PointerContact pointer = provider.GetPointer(0);
        Check(pointer.Id == PointerId.Mouse && pointer.Kind == PointerKind.Mouse &&
              pointer.IsDown && pointer.IsPrimary,
            "The default adapter preserves mouse identity, position, and pressed state");
        Near(new Vector2((float)pointer.Position.X, (float)pointer.Position.Y),
            new Vector2(120f, 80f));
    }

    private static void GeometryAndRevision()
    {
        var camera = new Camera2D(new Vector2(800f, 600f));
        var viewport = new ViewportController(camera);
        ViewportChangedEvent lastChange = default;
        viewport.Changed += change => lastChange = change;
        Near(viewport.Center, new Vector2(400f, 300f));
        Near(viewport.VisibleWorldBounds.Width, 800f);
        Near(viewport.VisibleWorldBounds.Height, 600f);
        Check(viewport.Revision == 0, "Fresh controller begins at revision zero");

        viewport.MoveCenter(new Vector2(1_000f, 700f));
        Near(viewport.Center, new Vector2(1_000f, 700f));
        Check(viewport.Revision == 1, "MoveCenter advances revision once");

        viewport.MoveCorner(new Vector2(50f, 80f));
        Near(new Vector2(viewport.VisibleWorldBounds.Left, viewport.VisibleWorldBounds.Top),
            new Vector2(50f, 80f));
        viewport.SetZoom(2f);
        Near(viewport.Center, new Vector2(450f, 380f));
        Near(viewport.VisibleWorldWidth, 400f);
        Near(viewport.VisibleWorldHeight, 300f);

        viewport.FitWorld(new Bounds2D(0f, 0f, 1_600f, 600f));
        Near(viewport.Zoom, 0.5f);
        Near(viewport.Center, new Vector2(800f, 300f));

        ulong stableRevision = viewport.Revision;
        viewport.Update(ViewportInputFrame.Empty, 1d / 60d);
        Check(viewport.Revision == stableRevision, "Stable update does not advance revision");
        camera.Position += Vector2.One;
        viewport.Update(ViewportInputFrame.Empty, 1d / 60d);
        Check(viewport.Revision == stableRevision + 1,
            "External Camera mutation is observed exactly once");
        Near(lastChange.PreviousCenter, new Vector2(800f, 300f));
        Near(lastChange.Center, new Vector2(801f, 301f));

        camera.ResizeViewport(1_000f, 600f);
        viewport.Resize();
        Check(lastChange.Kind == ViewportChangeKind.Resize,
            "Resize is reported distinctly from a programmatic Camera mutation");
        Near(lastChange.PreviousCenter, new Vector2(801f, 301f));
        Near(lastChange.Center, new Vector2(1_001f, 301f));
    }

    private static void PluginManagerLifecycle()
    {
        var viewport = CreateViewport();
        var log = new List<string>();
        viewport.Plugins.Add(new ProbePlugin("late", 20, log));
        viewport.Plugins.Add(new ProbePlugin("early", 10, log));
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Check(log.SequenceEqual(["early", "late"]), "Plugins run in stable order");

        log.Clear();
        viewport.Plugins.Pause("early");
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Check(log.SequenceEqual(["late"]), "Paused plugin is skipped");
        viewport.Plugins.Resume("early");
        Check(viewport.Plugins.Remove("late") && !viewport.Plugins.Remove("missing"),
            "Remove reports whether a plugin existed");
        viewport.Plugins.Add(new ProbePlugin("early", 10, log));
        Check(viewport.Plugins.Count == 1, "Adding the same key replaces rather than duplicates");
        viewport.Plugins.RemoveAll();
        Check(viewport.Plugins.Count == 0, "RemoveAll clears plugin set");
    }

    private static void DragAndDeceleration()
    {
        var viewport = CreateViewport();
        viewport.Plugins.Add(new ViewportDragPlugin(ViewportDragOptions.Default));
        var decelerate = new ViewportDeceleratePlugin(
            new ViewportDecelerateOptions(0.98f, 0.01f));
        viewport.Plugins.Add(decelerate);

        const double dt = 1d / 60d;
        viewport.Update(Frame(100f, 100f, down: true, pressed: true), dt);
        viewport.Update(Frame(160f, 100f, down: true), dt);
        Near(viewport.Position.X, -60f);
        viewport.Update(Frame(160f, 100f, down: false), dt);
        Check(decelerate.IsActive && decelerate.Velocity.X < 0f,
            "Release transfers drag velocity to deceleration");
        float released = viewport.Position.X;
        viewport.Update(Frame(160f, 100f, down: false), dt);
        Check(viewport.Position.X < released &&
              MathF.Abs(decelerate.Velocity.X) < 3_600f,
            "Deceleration continues in drag direction while velocity decays");

        var slow = CreateViewport();
        var slowPlugin = new ViewportDeceleratePlugin(
            new ViewportDecelerateOptions(0.98f, 0f));
        slow.Plugins.Add(slowPlugin);
        slowPlugin.Activate(new Vector2(600f, 0f));
        slow.Update(ViewportInputFrame.Empty, 1d / 30d);

        var fast = CreateViewport();
        var fastPlugin = new ViewportDeceleratePlugin(
            new ViewportDecelerateOptions(0.98f, 0f));
        fast.Plugins.Add(fastPlugin);
        fastPlugin.Activate(new Vector2(600f, 0f));
        fast.Update(ViewportInputFrame.Empty, 1d / 60d);
        fast.Update(ViewportInputFrame.Empty, 1d / 60d);
        Near(slow.Position, fast.Position, 0.001f);
        Near(slowPlugin.Velocity, fastPlugin.Velocity, 0.001f);
    }

    private static void DragThresholdAndAxis()
    {
        var viewport = CreateViewport();
        viewport.Plugins.Add(new ViewportDragPlugin(
            new ViewportDragOptions(ViewportAxis.Horizontal, 10f)));
        viewport.Update(Frame(100f, 100f, true, pressed: true), 1d / 60d);
        viewport.Update(Frame(105f, 105f, true), 1d / 60d);
        Near(viewport.Position, Vector2.Zero);
        viewport.Update(Frame(120f, 140f, true), 1d / 60d);
        Near(viewport.Position, new Vector2(-15f, 0f));
    }

    private static void PinchBehavior()
    {
        var viewport = CreateViewport();
        viewport.Plugins.Add(new ViewportDragPlugin(ViewportDragOptions.Default));
        var pinch = new ViewportPinchPlugin(ViewportPinchOptions.Default);
        viewport.Plugins.Add(pinch);

        ViewportPointer[] started =
        [
            Touch(1, 200f, 200f, pressed: true),
            Touch(2, 400f, 200f, pressed: true),
        ];
        viewport.Update(new ViewportInputFrame(started, Vector2.Zero, false, 0f), 1d / 60d);
        Check(pinch.IsPinching, "Two routed touch contacts begin Pinch");

        viewport.Camera.TryViewportToWorld(new Vector2(300f, 200f), out Vector2 before);
        ViewportPointer[] expanded =
        [
            Touch(1, 150f, 220f),
            Touch(2, 450f, 220f),
        ];
        viewport.Update(new ViewportInputFrame(expanded, Vector2.Zero, false, 0f), 1d / 60d);
        Near(viewport.Zoom, 1.5f);
        viewport.Camera.TryViewportToWorld(new Vector2(300f, 220f), out Vector2 after);
        Near(before, after, 0.001f);

        ViewportPointer[] oneRemaining = [Touch(1, 170f, 220f)];
        viewport.Update(new ViewportInputFrame(oneRemaining, Vector2.Zero, false, 0f), 1d / 60d);
        Check(!pinch.IsPinching, "Pinch ends when either captured contact disappears");
        viewport.Update(new ViewportInputFrame(oneRemaining, Vector2.Zero, false, 0f), 1d / 60d);
        float beforeDrag = viewport.Position.X;
        ViewportPointer[] dragged = [Touch(1, 200f, 220f)];
        viewport.Update(new ViewportInputFrame(dragged, Vector2.Zero, false, 0f), 1d / 60d);
        Check(viewport.Position.X < beforeDrag,
            "The remaining touch resumes Drag without requiring a synthetic release");
    }

    private static void WheelBehavior()
    {
        var viewport = CreateViewport();
        viewport.Plugins.Add(new ViewportWheelPlugin(ViewportWheelOptions.Default));
        Vector2 anchor = new(240f, 180f);
        viewport.Camera.TryViewportToWorld(anchor, out Vector2 before);
        viewport.Update(ScrollFrame(anchor, 1f), 1d / 60d);
        viewport.Camera.TryViewportToWorld(anchor, out Vector2 after);
        Near(viewport.Zoom, 1.1f);
        Near(before, after, 0.001f);

        var smooth = CreateViewport();
        smooth.Plugins.Add(new ViewportWheelPlugin(
            new ViewportWheelOptions(0.1f, smoothFrames: 4)));
        smooth.Update(ScrollFrame(anchor, 1f), 1d / 60d);
        Check(smooth.Zoom > 1f && smooth.Zoom < 1.1f, "Smooth wheel uses intermediate zoom");
        for (int i = 0; i < 3; i++)
            smooth.Update(ScrollFrame(anchor, 0f), 1d / 60d);
        Near(smooth.Zoom, 1.1f);

        var reverse = CreateViewport();
        reverse.Plugins.Add(new ViewportWheelPlugin(
            new ViewportWheelOptions(reverse: true)));
        reverse.Update(ScrollFrame(anchor, 1f), 1d / 60d);
        Check(reverse.Zoom < 1f, "Reverse wheel flips zoom direction");
    }

    private static void ClampZoomBehavior()
    {
        var viewport = CreateViewport(1_200f, 800f);
        viewport.Plugins.Add(new ViewportClampZoomPlugin(
            new ViewportClampZoomOptions(
                minWidth: 240f,
                maxWidth: 12_000f,
                maxHeight: 12_000f,
                maxScale: 8f)));
        viewport.SetZoom(0.01f);
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(viewport.Zoom, 0.1f);
        viewport.SetZoom(20f);
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(viewport.Zoom, 5f);

        viewport.Camera.ResizeViewport(2_400f, 800f);
        viewport.Resize();
        Near(viewport.Zoom, 5f);
        viewport.SetZoom(0.1f);
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(viewport.Zoom, 0.2f);
    }

    private static void ClampBehavior()
    {
        var world = new Bounds2D(0f, 0f, 1_200f, 1_000f);
        var viewport = CreateViewport(800f, 600f);
        viewport.Plugins.Add(new ViewportClampPlugin(new ViewportClampOptions(world)));
        viewport.MoveCorner(new Vector2(-500f, -400f));
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(new Vector2(viewport.VisibleWorldBounds.Left, viewport.VisibleWorldBounds.Top),
            Vector2.Zero);
        viewport.MoveCorner(new Vector2(900f, 800f));
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(new Vector2(viewport.VisibleWorldBounds.Right, viewport.VisibleWorldBounds.Bottom),
            new Vector2(1_200f, 1_000f));

        viewport.SetZoom(0.5f);
        viewport.Update(ViewportInputFrame.Empty, 0d);
        Near(viewport.Center, new Vector2(600f, 500f));

        var topLeft = CreateViewport(800f, 600f);
        topLeft.SetZoom(0.5f);
        topLeft.Plugins.Add(new ViewportClampPlugin(new ViewportClampOptions(
            world, underflow: ViewportUnderflow.TopLeft)));
        topLeft.Update(ViewportInputFrame.Empty, 0d);
        Near(new Vector2(topLeft.VisibleWorldBounds.Left, topLeft.VisibleWorldBounds.Top),
            Vector2.Zero);
    }

    private static void DeclarativeConfiguration()
    {
        ViewportNavigationConfiguration configuration = new ViewportNavigationBuilder()
            .Drag()
            .Pinch()
            .Wheel(new ViewportWheelOptions(smoothFrames: 3))
            .Decelerate()
            .ClampZoom(new ViewportClampZoomOptions(maxWidth: 12_000f, maxHeight: 12_000f))
            .Clamp(new ViewportClampOptions(new Bounds2D(0f, 0f, 12_000f, 12_000f)))
            .Build();
        ViewportController viewport = configuration.CreateController(
            new Camera2D(new Vector2(1_200f, 800f)));
        Check(viewport.Plugins.Count == 6 &&
              viewport.Plugins.Get<ViewportDragPlugin>(ViewportPluginKeys.Drag) is not null &&
              viewport.Plugins.Get<ViewportPinchPlugin>(ViewportPluginKeys.Pinch) is not null &&
              viewport.Plugins.Get<ViewportClampPlugin>(ViewportPluginKeys.Clamp) is not null,
            "Builder creates the desktop golden plugin chain");
        Near(viewport.Zoom, 1f);
    }

    private static void Validation()
    {
        Throws<ArgumentOutOfRangeException>(() => new ViewportWheelOptions(percent: 0f));
        Throws<ArgumentOutOfRangeException>(() => new ViewportPinchOptions(zoomSpeed: 0f));
        Throws<ArgumentOutOfRangeException>(() => new ViewportDecelerateOptions(friction: 1f));
        Throws<ArgumentException>(() => new ViewportClampZoomOptions(
            minWidth: 100f, maxWidth: 50f));
        Throws<ArgumentException>(() => new ViewportClampPlugin(new ViewportClampOptions(
            new Bounds2D(0f, 0f, 0f, 10f))));
        Throws<InvalidOperationException>(() => new ViewportNavigationBuilder().Build());
        Throws<ArgumentOutOfRangeException>(() => CreateViewport().Update(
            ViewportInputFrame.Empty, -1d));
    }

    private static void StableUpdateAllocation()
    {
        var viewport = CreateViewport();
        viewport.Plugins.Add(new ViewportClampZoomPlugin(
            new ViewportClampZoomOptions(minScale: 0.2f, maxScale: 5f)));
        viewport.Plugins.Add(new ViewportClampPlugin(new ViewportClampOptions(
            new Bounds2D(0f, 0f, 12_000f, 12_000f))));
        viewport.MoveCenter(new Vector2(6_000f, 6_000f));
        for (int i = 0; i < 256; i++)
            viewport.Update(ViewportInputFrame.Empty, 1d / 60d);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            viewport.Update(ViewportInputFrame.Empty, 1d / 60d);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
                viewport.Update(ViewportInputFrame.Empty, 1d / 60d);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(allocated == 0, $"Stable Viewport updates allocate 0 B, actual {allocated:N0} B");

        var touchViewport = CreateViewport();
        touchViewport.Plugins.Add(new ViewportPinchPlugin(ViewportPinchOptions.Default));
        Span<ViewportPointer> touches = stackalloc ViewportPointer[2];
        touches[0] = Touch(1, 200f, 200f, pressed: true);
        touches[1] = Touch(2, 400f, 200f, pressed: true);
        var start = new ViewportInputFrame(touches, Vector2.Zero, false, 0f);
        touchViewport.Update(in start, 1d / 60d);
        for (int i = 0; i < 256; i++)
        {
            float edge = (i & 1) == 0 ? 400f : 401f;
            touches[0] = Touch(1, 200f, 200f);
            touches[1] = Touch(2, edge, 200f);
            var frame = new ViewportInputFrame(touches, Vector2.Zero, false, 0f);
            touchViewport.Update(in frame, 1d / 60d);
        }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            float edge = (i & 1) == 0 ? 400f : 401f;
            touches[0] = Touch(1, 200f, 200f);
            touches[1] = Touch(2, edge, 200f);
            var frame = new ViewportInputFrame(touches, Vector2.Zero, false, 0f);
            touchViewport.Update(in frame, 1d / 60d);
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0,
            $"Stable multi-pointer Pinch updates allocate 0 B, actual {allocated:N0} B");
    }

    private static ViewportController CreateViewport(float width = 800f, float height = 600f) =>
        new(new Camera2D(new Vector2(width, height)));

    private static ViewportInputFrame Frame(
        float x,
        float y,
        bool down,
        bool pressed = false)
    {
        ViewportPointer[] pointers =
        [
            new ViewportPointer(
                PointerId.Mouse,
                PointerKind.Mouse,
                new Vector2(x, y),
                isInside: true,
                isCaptured: down,
                down,
                isPrimary: true,
                wasPressed: pressed),
        ];
        return new ViewportInputFrame(pointers, new Vector2(x, y), true, 0f);
    }

    private static ViewportInputFrame ScrollFrame(Vector2 position, float delta) =>
        new(ReadOnlySpan<ViewportPointer>.Empty, position, true, delta);

    private static ViewportPointer Touch(
        long id,
        float x,
        float y,
        bool pressed = false) => new(
            new PointerId(id),
            PointerKind.Touch,
            new Vector2(x, y),
            isInside: true,
            isCaptured: true,
            isDown: true,
            isPrimary: id == 1,
            wasPressed: pressed);

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

    private static void Near(float actual, float expected, float epsilon = 0.0001f)
    {
        if (MathF.Abs(actual - expected) > epsilon)
            throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }

    private static void Near(Vector2 actual, Vector2 expected, float epsilon = 0.001f)
    {
        if (Vector2.Distance(actual, expected) > epsilon)
            throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class ProbePlugin(string key, int order, List<string> log)
        : ViewportPlugin(key, order)
    {
        protected override void OnUpdate(
            ViewportController controller,
            in ViewportInputFrame input,
            double deltaTime) => log.Add(Key);
    }

    private sealed class MouseOnlyInputProvider : IInputProvider
    {
        public Vector2D MousePosition => new(120, 80);
        public float MouseScrollDelta => 0f;
        public bool IsKeyDown(InputKey key) => false;
        public bool IsMouseButtonDown(MouseButton button) => button == MouseButton.Left;
    }
}
