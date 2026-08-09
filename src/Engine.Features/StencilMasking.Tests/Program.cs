namespace StencilMasking.Tests;

using GameEngine.Core.Application.Commands;
using GameEngine.Core.Application.Handlers;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.StencilMasking.Application;
using GameEngine.Features.StencilMasking.Domain;

internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (condition) Console.WriteLine($"  [PASS] {name}");
        else
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }

    private static void Main()
    {
        Console.WriteLine("=== StencilMasking Feature Smoke Test ===\n");
        TestState();
        TestTypedEffectEvents();
        TestMaskGeometry();
        TestOwnerAggregation();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All StencilMasking smoke tests passed ==="
            : $"=== {_failures} StencilMasking test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void TestState()
    {
        Console.WriteLine("1. StencilMaskState");
        Check(StencilMaskState.Default.Mode == StencilMaskMode.ShowInside &&
              StencilMaskState.Default.StencilRef == 1,
            "Default = ShowInside, ref=1");
        Check(StencilMaskState.FogOfWarHole.Inverted.Mode == StencilMaskMode.ShowInside,
            "Inverted mode is stable");
        var states = new HashSet<StencilMaskState>
        {
            StencilMaskState.Spotlight,
            StencilMaskState.Spotlight,
            StencilMaskState.FogOfWarHole
        };
        Check(states.Count == 2, "Stencil state remains a value-object key");
    }

    private static void TestTypedEffectEvents()
    {
        Console.WriteLine("2. Typed persistent effect events");
        var scene = new SceneAggregate("StencilScene");
        var player = SceneCommandHandlers.Handle(new SpawnInstanceCommand(
            scene,
            "Player",
            new Vector2D(100, 100),
            LayerDepth.Instances));
        scene.MarkEventsAsCommitted();

        StencilMaskCommandHandler.Handle(new ApplySpotlightMaskCommand(
            scene,
            player.Id,
            new Vector2D(120, 80),
            64f,
            StencilMaskState.Spotlight));

        var requested = scene.UncommittedEvents.OfType<RenderEffectRequestedEvent>().SingleOrDefault();
        var descriptor = requested?.Descriptor as StencilMaskEffectDescriptor;
        Check(requested?.OwnerId == player.Id, "Command raises owner-scoped RenderEffectRequestedEvent");
        Check(descriptor is
              {
                  Center: var center,
                  Radius: 64f,
                  State: var state
              } && center == new Vector2D(120, 80) && state == StencilMaskState.Spotlight,
            "Typed descriptor carries geometry and stencil state without callbacks");

        bool invalidRadiusRejected = false;
        try
        {
            _ = new StencilMaskEffectDescriptor(
                StencilMaskEffectDescriptor.DefaultKey,
                Vector2D.Zero,
                0f,
                StencilMaskState.Default);
        }
        catch (ArgumentOutOfRangeException) { invalidRadiusRejected = true; }
        Check(invalidRadiusRejected, "Non-positive radius is rejected at declaration time");

        player.SetActive(false, scene.RaiseEvent);
        scene.MarkEventsAsCommitted();
        StencilMaskCommandHandler.Handle(new ApplySpotlightMaskCommand(
            scene,
            player.Id,
            Vector2D.Zero,
            10f,
            StencilMaskState.Default));
        Check(!scene.UncommittedEvents.OfType<RenderEffectRequestedEvent>().Any(),
            "Inactive instance does not request an effect");

        player.ReleaseStencilMask(scene.RaiseEvent);
        Check(scene.UncommittedEvents.OfType<RenderEffectReleasedEvent>()
                .Any(effect => effect.OwnerId == player.Id),
            "Explicit release remains available during teardown");
    }

    private static void TestOwnerAggregation()
    {
        Console.WriteLine("4. Shared owner aggregation policy");
        var key = StencilMaskEffectDescriptor.DefaultKey;
        var ownerA = InstanceId.New();
        var ownerB = InstanceId.New();
        var owners = new Dictionary<InstanceId, IRenderEffectDescriptor>
        {
            [ownerB] = new StencilMaskEffectDescriptor(
                key, new Vector2D(200, 100), 50f, StencilMaskState.Spotlight),
            [ownerA] = new StencilMaskEffectDescriptor(
                key, new Vector2D(100, 100), 40f, StencilMaskState.Spotlight)
        };

        var ordered = StencilMaskEffectPolicy.ValidateAndOrder(key, owners);
        Check(ordered.Length == 2, "Multiple owners share one stencil effect");
        var expectedFirst = ownerA.CompareTo(ownerB) < 0
            ? new Vector2D(100, 100)
            : new Vector2D(200, 100);
        Check(ordered[0].Center == expectedFirst,
            "Owner aggregation order is deterministic");

        owners[ownerB] = new StencilMaskEffectDescriptor(
            key, new Vector2D(200, 100), 50f, StencilMaskState.FogOfWarHole);
        bool conflictRejected = false;
        try { StencilMaskEffectPolicy.ValidateAndOrder(key, owners); }
        catch (InvalidOperationException) { conflictRejected = true; }
        Check(conflictRejected, "Owners sharing a key cannot conflict on stencil state");
    }

    private static void TestMaskGeometry()
    {
        Console.WriteLine("3. Circle and Sprite Alpha geometry");
        var circle = StencilMaskGeometry.Circle(new Vector2D(12, 24), 8f);
        Check(circle.Kind == StencilMaskGeometryKind.Circle &&
              circle.Center == new Vector2D(12, 24) &&
              circle.Radius == 8f,
            "Circle is a typed procedural geometry");

        var sprite = new SpriteRef("mask.cooldown-ring");
        var transform = new Transform2D(
            new Vector2D(100, 80),
            MathF.PI / 4f,
            new Vector2D(-2f, 1.5f));
        var spriteMask = StencilMaskGeometry.FromSprite(sprite, -1f, transform, 0.35f);
        Check(spriteMask.Kind == StencilMaskGeometryKind.SpriteAlpha &&
              spriteMask.Sprite == sprite &&
              spriteMask.SubImage == -1f &&
              spriteMask.Transform == transform &&
              spriteMask.AlphaCutoff == 0.35f,
            "Sprite Alpha preserves sub-image, origin-aware transform, and cutoff");

        var events = new List<GameEngine.Core.Domain.Events.IDomainEvent>();
        var instance = new GameEngine.Core.Domain.Entities.GameInstance(
            "sprite-mask-owner", Vector2D.Zero, LayerDepth.Instances);
        instance.RequestStencilSpriteMask(
            sprite,
            2f,
            transform,
            0.5f,
            StencilMaskState.Spotlight,
            events.Add);
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: StencilMaskEffectDescriptor
                {
                    Geometry.Kind: StencilMaskGeometryKind.SpriteAlpha
                }
            },
            "GameInstance raises a typed Sprite Alpha request");

        CheckThrows<ArgumentException>(
            () => StencilMaskGeometry.FromSprite(SpriteRef.Empty, 0f, transform),
            "Empty Sprite masks are rejected");
        CheckThrows<ArgumentException>(
            () => StencilMaskGeometry.FromSprite(
                sprite,
                0f,
                transform with { Scale = Vector2D.Zero }),
            "Zero Sprite mask scale is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => StencilMaskGeometry.FromSprite(sprite, 0f, transform, 1.01f),
            "Alpha cutoff outside [0,1] is rejected");
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
