namespace Sprites.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Sprites.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Sprites Feature Smoke Test ===\n");
        VerifyLibrary();
        VerifyAnimation();
        VerifyGeometry();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Sprites smoke tests passed ==="
            : $"=== {_failures} Sprites test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyLibrary()
    {
        Console.WriteLine("1. SpriteLibrary registration and frame resolution");
        var library = new SpriteLibrary();
        var single = library.RegisterSingle("single", 1u, new Vector2(32, 16), new Vector2(16, 8));
        Check(library.TryGetMetadata(single, out var singleMeta) &&
              singleMeta.FrameCount == 1 && singleMeta.Origin == new Vector2(16, 8),
            "Single-frame metadata");

        var frames = library.RegisterFrames("frames", 2u, new Vector2(16), new Vector2(8),
            new[] { new Vector4(0, 0, .5f, 1), new Vector4(.5f, 0, 1, 1) }, 6f);
        Check(library.TryResolve(frames, 3, out var wrapped) && wrapped.UvBounds.X == .5f,
            "Overflow frame wraps");
        Check(library.TryResolve(frames, -1, out var reverse) && reverse.UvBounds.X == .5f,
            "Negative frame wraps");

        var grid = library.RegisterGrid("grid", 3u, new Vector2(64, 32), new Vector2(16, 16),
            new Vector2(8), frameCount: 6, framesPerSecond: 8f);
        library.TryResolve(grid, 5, out var gridFrame);
        Check(Near(gridFrame.UvBounds, new Vector4(.25f, .5f, .5f, 1f)),
            "Grid frames are row-major");
        Check(library.TryGetMetadata(grid, out var gridMeta) && gridMeta.FramesPerSecond == 8f,
            "Animation FPS metadata");
        Check(!library.TryResolve(new SpriteRef("missing"), 0, out _),
            "Unknown sprite resolves safely");

        CheckThrows<ArgumentException>(() =>
            library.RegisterSingle("single", 9u, Vector2.One, Vector2.Zero),
            "Duplicate registration rejected");
        CheckThrows<ArgumentException>(() =>
            library.RegisterFrames("empty", 9u, Vector2.One, Vector2.Zero, Array.Empty<Vector4>()),
            "Empty frame list rejected");
        CheckThrows<ArgumentOutOfRangeException>(() =>
            library.RegisterSingle("bad-size", 9u, Vector2.Zero, Vector2.Zero),
            "Invalid size rejected");
    }

    private static void VerifyAnimation()
    {
        Console.WriteLine("2. GameInstance animation advancement");
        var library = new SpriteLibrary();
        var animated = library.RegisterGrid("animated", 1u, new Vector2(64, 16), new Vector2(16),
            new Vector2(8), frameCount: 4, framesPerSecond: 4f);
        var scene = new SceneAggregate("SpriteAnimation");
        scene.SetSprites(library);
        var instance = scene.Add(new GameInstance("Animated", Vector2D.Zero, LayerDepth.Instances)
        {
            Sprite = animated
        });

        scene.PerformStep(.25);
        Check(Near(instance.ImageIndex, 1f), "ImageIndex advances by fps × speed × dt");
        instance.ImageSpeed = 0f;
        scene.PerformStep(1.0);
        Check(Near(instance.ImageIndex, 1f), "ImageSpeed zero pauses animation");
        instance.ImageIndex = 0f;
        instance.ImageSpeed = -1f;
        scene.PerformStep(.25);
        Check(Near(instance.ImageIndex, 3f), "Negative speed loops backwards");
        instance.SetActive(false, scene.RaiseEvent);
        scene.PerformStep(.25);
        Check(Near(instance.ImageIndex, 3f), "Inactive instance does not advance");
    }

    private static void VerifyGeometry()
    {
        Console.WriteLine("3. Origin / scale / rotation geometry");
        var frame = new ResolvedSpriteFrame(1u, new Vector2(10, 20), new Vector2(5, 10),
            new Vector4(0, 0, 1, 1));
        Span<Vector2> corners = stackalloc Vector2[4];

        var centered = new SpriteDrawCommand(new SpriteRef("s"), 0, new Vector2(100),
            Vector2.One, 0f, Vector4.One);
        SpriteGeometry.CalculateCorners(centered, frame, corners);
        Check(Near(corners[0], new Vector2(95, 90)) && Near(corners[2], new Vector2(105, 110)),
            "Custom origin anchors position");

        var rotated = centered with
        {
            Position = Vector2.Zero,
            Scale = new Vector2(1, 1),
            RotationRadians = MathF.PI / 2,
            OriginOverride = Vector2.Zero,
            SizeOverride = new Vector2(2)
        };
        SpriteGeometry.CalculateCorners(rotated, frame, corners);
        Check(Near(corners[1], new Vector2(0, -2)) && Near(corners[2], new Vector2(2, -2)),
            "Positive rotation is counter-clockwise in Y-down coordinates");

        var flipped = rotated with { RotationRadians = 0f, Scale = new Vector2(-1, 2) };
        SpriteGeometry.CalculateCorners(flipped, frame, corners);
        Check(Near(corners[1], new Vector2(-2, 0)) && Near(corners[2], new Vector2(-2, 4)),
            "Negative and non-uniform scale");
    }

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else { _failures++; Console.WriteLine($"  [FAIL] {name}"); }
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try { action(); Check(false, name); }
        catch (T) { Check(true, name); }
    }

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.0001f;
    private static bool Near(Vector2 a, Vector2 b) => Near(a.X, b.X) && Near(a.Y, b.Y);
    private static bool Near(Vector4 a, Vector4 b) =>
        Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z) && Near(a.W, b.W);
}
