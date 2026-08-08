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
        Console.WriteLine("3. Shared owner aggregation policy");
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
}
