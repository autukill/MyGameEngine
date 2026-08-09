namespace Sprites.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Sprites.Domain;
using GameEngine.Features.Sprites.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Sprites Feature Smoke Test ===\n");
        VerifyLibrary();
        VerifyAtomicReplacement();
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
        var textures = new TestTextureResolver();
        var singleTexture = textures.Add("single-texture", 1u, 32, 16);
        var framesTexture = textures.Add("frames-texture", 2u, 32, 16);
        var gridTexture = textures.Add("grid-texture", 3u, 64, 32);
        var pixelFrameA = textures.Add("pixel-frame-a", 10u, 20, 10);
        var pixelFrameB = textures.Add("pixel-frame-b", 11u, 40, 20);
        var library = new SpriteLibrary(textures);
        var single = library.RegisterSingle("single", singleTexture, new Vector2(16, 8));
        Check(library.TryGetMetadata(single, out var singleMeta) &&
              singleMeta.FrameCount == 1 && singleMeta.Origin == new Vector2(16, 8),
            "Single-frame metadata");

        var frames = library.RegisterFrames("frames", framesTexture, new Vector2(16), new Vector2(8),
            new[] { new Vector4(0, 0, .5f, 1), new Vector4(.5f, 0, 1, 1) }, 6f);
        Check(library.TryResolve(frames, 3, out var wrapped) && wrapped.UvBounds.X == .5f,
            "Overflow frame wraps");
        Check(library.TryResolve(frames, -1, out var reverse) && reverse.UvBounds.X == .5f,
            "Negative frame wraps");

        var grid = library.RegisterGrid("grid", gridTexture, new Vector2(16, 16),
            new Vector2(8), frameCount: 6, framesPerSecond: 8f);
        library.TryResolve(grid, 5, out var gridFrame);
        Check(Near(gridFrame.UvBounds, new Vector4(.25f, .5f, .5f, 1f)),
            "Grid frames are row-major");
        Check(library.TryGetMetadata(grid, out var gridMeta) && gridMeta.FramesPerSecond == 8f,
            "Animation FPS metadata");
        Check(!library.TryResolve(new SpriteRef("missing"), 0, out _),
            "Unknown sprite resolves safely");

        var pixelFrames = library.RegisterPixelFrames(
            "pixel-frames",
            new Vector2(8, 6),
            new Vector2(3, 4),
            new[]
            {
                new SpriteFrameSource(pixelFrameA, new PixelRectI(2, 1, 8, 6)),
                new SpriteFrameSource(pixelFrameB, new PixelRectI(16, 8, 8, 6))
            },
            12f);
        library.TryResolve(pixelFrames, 0, out var pixelA);
        library.TryResolve(pixelFrames, 1, out var pixelB);
        Check(pixelA.TextureHandle == 10u && pixelB.TextureHandle == 11u,
            "Pixel frames can resolve to different texture handles");
        Check(Near(pixelA.UvBounds, new Vector4(.1f, .1f, .5f, .7f)) &&
              Near(pixelB.UvBounds, new Vector4(.4f, .4f, .6f, .7f)),
            "Pixel rectangles convert to per-texture UV bounds");

        CheckThrows<ArgumentException>(() =>
            library.RegisterSingle("single", singleTexture, Vector2.Zero),
            "Duplicate registration rejected");
        CheckThrows<ArgumentException>(() =>
            library.RegisterFrames("empty", singleTexture, Vector2.One, Vector2.Zero, Array.Empty<Vector4>()),
            "Empty frame list rejected");
        CheckThrows<ArgumentOutOfRangeException>(() =>
            library.RegisterFrames("bad-size", singleTexture, Vector2.Zero, Vector2.Zero,
                new[] { new Vector4(0, 0, 1, 1) }),
            "Invalid size rejected");
        CheckThrows<ArgumentException>(() =>
            library.RegisterSingle("missing-texture", new TextureRef("missing"), Vector2.Zero),
            "Unknown texture is rejected during registration");
        CheckThrows<ArgumentException>(() =>
            library.RegisterPixelFrames("pixel-out-of-bounds", Vector2.One, Vector2.Zero,
                new[] { new SpriteFrameSource(pixelFrameA, new PixelRectI(19, 0, 2, 1)) }),
            "Out-of-bounds pixel frame is rejected");
        CheckThrows<ArgumentException>(() =>
            library.RegisterPixelFrames("pixel-mismatch", Vector2.One, Vector2.Zero,
                new[]
                {
                    new SpriteFrameSource(pixelFrameA, new PixelRectI(0, 0, 2, 2)),
                    new SpriteFrameSource(pixelFrameB, new PixelRectI(0, 0, 3, 2))
                }),
            "Mismatched pixel frame dimensions are rejected");
    }

    private static void VerifyAnimation()
    {
        Console.WriteLine("2. GameInstance animation advancement");
        var textures = new TestTextureResolver();
        var texture = textures.Add("animated-texture", 1u, 64, 16);
        var library = new SpriteLibrary(textures);
        var animated = library.RegisterGrid("animated", texture, new Vector2(16),
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

    private static void VerifyAtomicReplacement()
    {
        Console.WriteLine("2. Atomic Sprite replacement");
        var textures = new TestTextureResolver();
        var texture = textures.Add("replacement-texture", 20u, 16, 8);
        var library = new SpriteLibrary(textures);
        var sprite = library.RegisterSingle("live", texture, new Vector2(1, 2));

        using (var replacement = library.BeginReplacement(
                   new[] { "live" },
                   new[] { new SpriteReplacementSource(
                       "live",
                       new Vector2(8),
                       new Vector2(4),
                       [new SpriteFrameSource(texture, new PixelRectI(8, 0, 8, 8))],
                       12f) }))
        {
            library.TryGetMetadata(sprite, out var before);
            Check(before.Origin == new Vector2(1, 2),
                "Staged Sprite is invisible before activation");
            replacement.Activate();
            library.TryGetMetadata(sprite, out var active);
            Check(active.Origin == new Vector2(4) && active.FramesPerSecond == 12f,
                "Activation updates an existing logical SpriteRef");
        }
        library.TryGetMetadata(sprite, out var restored);
        Check(restored.Origin == new Vector2(1, 2),
            "Uncommitted Sprite replacement restores the previous entry");

        using (var replacement = library.BeginReplacement(
                   new[] { "live" },
                   Array.Empty<SpriteReplacementSource>()))
        {
            replacement.Activate();
            replacement.Commit();
        }
        Check(!library.TryGetMetadata(sprite, out _),
            "Committed replacement can remove a Sprite from its ownership scope");
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

    private sealed class TestTextureResolver : ITextureResolver
    {
        private readonly Dictionary<string, ResolvedTexture> _textures = new(StringComparer.Ordinal);

        public TextureRef Add(string name, uint handle, int width, int height)
        {
            var texture = new TextureRef(name);
            _textures.Add(name, new ResolvedTexture(handle, new TextureMetadata(width, height)));
            return texture;
        }

        public bool TryGetMetadata(TextureRef texture, out TextureMetadata metadata)
        {
            if (!texture.IsEmpty && _textures.TryGetValue(texture.Name, out var resolved))
            {
                metadata = resolved.Metadata;
                return true;
            }

            metadata = default;
            return false;
        }

        public bool TryResolve(TextureRef texture, out ResolvedTexture resolved)
        {
            if (!texture.IsEmpty && _textures.TryGetValue(texture.Name, out resolved))
                return true;

            resolved = default;
            return false;
        }
    }
}
