namespace Presentation.Tests;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Presentation.Application;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Presentation Feature Smoke Test ===\n");
        TestDescriptor();
        TestInstanceEvents();
        TestOrderingAndDeduplication();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Presentation smoke tests passed ==="
            : $"=== {_failures} Presentation test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestDescriptor()
    {
        Console.WriteLine("1. Descriptor validation");
        var descriptor = new PresentSurfaceDescriptor(
            PresentSurfaceDescriptor.DefaultKey,
            RenderSurfaceKey.SceneGui,
            ViewportRect.FullScreen,
            1000,
            PresentationBlendMode.AlphaBlend);
        Check(descriptor.Key == new RenderEffectKey("present", "main") &&
              descriptor.Source == RenderSurfaceKey.SceneGui &&
              descriptor.Layer == 1000,
            "Descriptor identifies the unique terminal and its logical source");
        CheckThrows<ArgumentException>(
            () => new PresentSurfaceDescriptor(
                new RenderEffectKey("present", "secondary"),
                RenderSurfaceKey.SceneGui,
                ViewportRect.FullScreen,
                0,
                PresentationBlendMode.Opaque),
            "A second screen terminal is rejected");
        CheckThrows<ArgumentException>(
            () => new PresentSurfaceDescriptor(
                PresentSurfaceDescriptor.DefaultKey,
                default,
                ViewportRect.FullScreen,
                0,
                PresentationBlendMode.Opaque),
            "An uninitialized source is rejected");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new PresentSurfaceDescriptor(
                PresentSurfaceDescriptor.DefaultKey,
                RenderSurfaceKey.SceneGui,
                new ViewportRect(0.75f, 0f, 0.5f, 1f),
                0,
                PresentationBlendMode.Opaque),
            "A viewport escaping normalized screen bounds is rejected");
    }

    private static void TestInstanceEvents()
    {
        Console.WriteLine("2. GameInstance events");
        var events = new List<IDomainEvent>();
        var instance = new GameInstance("present-owner", Vector2D.Zero, LayerDepth.Instances);
        instance.RequestPresentSurface(
            RenderSurfaceKey.SceneGui,
            events.Add,
            layer: 7,
            blend: PresentationBlendMode.Additive,
            viewport: ViewportRect.TopRightQuarter);
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: PresentSurfaceDescriptor
                {
                    Source: var source,
                    Layer: 7,
                    Blend: PresentationBlendMode.Additive,
                    Viewport: var viewport
                }
            } && source == RenderSurfaceKey.SceneGui &&
                 viewport == ViewportRect.TopRightQuarter,
            "Active instances request typed presentation entries");
        events.Clear();
        instance.ReleasePresentSurface(events.Add);
        Check(events.Single() is RenderEffectReleasedEvent released &&
              released.EffectKey == PresentSurfaceDescriptor.DefaultKey,
            "Release addresses the unique terminal");
        instance.SetActive(false, _ => { });
        events.Clear();
        instance.RequestPresentSurface(RenderSurfaceKey.SceneGui, events.Add);
        Check(events.Count == 0, "Inactive instances cannot request presentation");
    }

    private static void TestOrderingAndDeduplication()
    {
        Console.WriteLine("3. Stable ordering and deduplication");
        var firstOwner = new InstanceId(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var duplicateOwner = new InstanceId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var topOwner = new InstanceId(Guid.Parse("00000000-0000-0000-0000-000000000003"));
        var tone = new RenderSurfaceKey("tone", "main", "color");
        var owners = new Dictionary<InstanceId, IRenderEffectDescriptor>
        {
            [topOwner] = Descriptor(RenderSurfaceKey.SceneGui, 1000, PresentationBlendMode.AlphaBlend),
            [duplicateOwner] = Descriptor(tone, 0, PresentationBlendMode.Opaque),
            [firstOwner] = Descriptor(tone, 0, PresentationBlendMode.Opaque)
        };

        var entries = PresentSurfacePolicy.ValidateOrderAndDeduplicate(
            PresentSurfaceDescriptor.DefaultKey,
            owners);
        Check(entries.Length == 2 &&
              entries[0].Source == tone &&
              entries[0].FirstOwner == firstOwner &&
              entries[1].Source == RenderSurfaceKey.SceneGui,
            "Entries sort by layer then owner and collapse identical requests");

        owners[duplicateOwner] = Descriptor(tone, 1, PresentationBlendMode.Additive);
        entries = PresentSurfacePolicy.ValidateOrderAndDeduplicate(
            PresentSurfaceDescriptor.DefaultKey,
            owners);
        Check(entries.Length == 3 && entries[1].Layer == 1,
            "Different layer or blend remains an independent presentation entry");
    }

    private static PresentSurfaceDescriptor Descriptor(
        RenderSurfaceKey source,
        int layer,
        PresentationBlendMode blend) => new(
            PresentSurfaceDescriptor.DefaultKey,
            source,
            ViewportRect.FullScreen,
            layer,
            blend);

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
