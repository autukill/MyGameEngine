namespace GameEngine.Hosting.Tests;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Domain;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Engine Hosting Smoke Test ===\n");
        TestBuilderPlans();
        TestBuilderValidation();
        TestResourceOwnership();
        TestDefaultPresentationControllers();
        TestPerformanceTelemetry();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Engine Hosting smoke tests passed ==="
            : $"=== {_failures} Engine Hosting test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestBuilderPlans()
    {
        Console.WriteLine("1. Immutable application plans");
        var bloom = new BloomSettings(0.4f, 1.4f, 1f, 2, BloomResolution.Half);
        var tone = new ToneMappingSettings(ToneMappingOperator.Aces, 0.5f, 2.2f);
        var package = new ContentPackageRef("game.assets", "game/assets.json");
        var plan = GameApplication.Create(new EngineWindowOptions(Title: "Hosting Test"))
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(package)
                .UseHdr(tone, bloom)
                .EnableStencilMasking())
            .ConfigureScene("Main", _ => { })
            .BuildPlan();

        Check(plan.WindowOptions.Title == "Hosting Test" &&
              plan.SceneName == "Main" &&
              plan.Renderer.ContentPackagesRoot == "AssetsCompiled" &&
              plan.Renderer.ContentManifest == "game/assets.json" &&
              plan.Renderer.ContentPackage == package &&
              plan.Renderer.HdrEnabled &&
              plan.Renderer.Bloom == bloom &&
              plan.Renderer.ToneMapping == tone &&
              plan.Renderer.StencilMaskingEnabled &&
              plan.Renderer.SceneGuiEnabled,
            "Builder freezes window, content, HDR, Bloom, Stencil, and Scene configuration");

        var ldr = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.DisableSceneGui())
            .ConfigureScene("Ldr", _ => { })
            .BuildPlan();
        Check(!ldr.Renderer.HdrEnabled &&
              ldr.Renderer.Bloom is null &&
              !ldr.Renderer.SceneGuiEnabled,
            "Default renderer remains LDR and optional features are lazy");
    }

    private static void TestBuilderValidation()
    {
        Console.WriteLine("2. Fail-fast configuration validation");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .ConfigureScene("Main", _ => { })
                .BuildPlan(),
            "Missing renderer is rejected before creating a window");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .BuildPlan(),
            "Missing initial Scene is rejected before creating a window");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .UseDefault2DRenderer(),
            "Duplicate default renderer registration is rejected");
        CheckThrows<ArgumentException>(
            () => new Default2DRendererOptions().UseContent(" "),
            "Empty content root is rejected");
        CheckThrows<ArgumentException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .ConfigureScene(" ", _ => { }),
            "Empty Scene name is rejected");
    }

    private static void TestResourceOwnership()
    {
        Console.WriteLine("3. Reverse-order resource ownership");
        var order = new List<string>();
        var stack = new OwnedResourceStack();
        stack.Add(new Probe("shader", order));
        stack.Add(new Probe("target", order));
        stack.Add(new Probe("builder", order));
        stack.Dispose();
        stack.Dispose();
        Check(order.SequenceEqual(new[] { "builder", "target", "shader" }),
            "Resources dispose once in reverse creation order");
        CheckThrows<ObjectDisposedException>(
            () => stack.Add(new Probe("late", order)),
            "Disposed ownership scope rejects late resources");

        order.Clear();
        var failing = new OwnedResourceStack();
        failing.Add(new Probe("first", order));
        failing.Add(new Probe("throws", order, fail: true));
        failing.Add(new Probe("last", order));
        CheckThrows<AggregateException>(failing.Dispose,
            "Disposal reports owned resource failures");
        Check(order.SequenceEqual(new[] { "last", "throws", "first" }),
            "A disposal failure does not skip remaining resources");
    }

    private static void TestDefaultPresentationControllers()
    {
        Console.WriteLine("4. Default renderer domain lifecycle");
        var hdrPlan = new Default2DRendererPlan(
            null,
            null,
            true,
            ToneMappingSettings.Default,
            BloomSettings.Default,
            true,
            true);
        var events = new List<IDomainEvent>();
        var hdr = new DefaultWorldPresentationController(events.Add, hdrPlan);
        hdr.OnCreate();
        Check(events.OfType<RenderEffectRequestedEvent>().Select(value => value.Descriptor.Key.Kind)
                .SequenceEqual(new[]
                {
                    BloomEffectDescriptor.EffectKind,
                    ToneMappingEffectDescriptor.EffectKind,
                    PresentSurfaceDescriptor.EffectKind
                }),
            "HDR preset declares Bloom, Tone Mapping, then Presentation");
        events.Clear();
        hdr.OnDestroy();
        Check(events.OfType<RenderEffectReleasedEvent>().Select(value => value.EffectKey.Kind)
                .SequenceEqual(new[]
                {
                    PresentSurfaceDescriptor.EffectKind,
                    ToneMappingEffectDescriptor.EffectKind,
                    BloomEffectDescriptor.EffectKind
                }),
            "HDR preset releases consumers before producers");

        events.Clear();
        var ldr = new DefaultWorldPresentationController(
            events.Add,
            hdrPlan with { HdrEnabled = false, Bloom = null });
        ldr.OnCreate();
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: PresentSurfaceDescriptor { Source: var source }
            } && source == RenderSurfaceKey.SceneColor,
            "LDR preset directly presents SceneColor without post-process resources");

        events.Clear();
        var gui = new DefaultGuiPresentationController(events.Add);
        gui.OnCreate();
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: PresentSurfaceDescriptor
                {
                    Source: var guiSource,
                    Layer: 1000
                }
            } && guiSource == RenderSurfaceKey.SceneGui,
            "SceneGui is declared as an exposure-independent top layer");
    }

    private static void TestPerformanceTelemetry()
    {
        Console.WriteLine("5. Performance budgets and low-frequency telemetry");
        var sink = new RecordingTelemetrySink();
        var telemetry = new PerformanceTelemetryOptions(
            sink,
            TimeSpan.FromSeconds(1),
            new PerformanceBudget(maxDrawCalls: 10));
        var plan = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.EnablePerformanceTelemetry(telemetry))
            .ConfigureScene("Telemetry", _ => { })
            .BuildPlan();
        Check(plan.Renderer.PerformanceTelemetry == telemetry &&
              plan.WindowOptions.FrameStatistics is not null,
            "Enabling telemetry freezes its plan and automatically enables frame statistics");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .EnablePerformanceTelemetry(telemetry)
                .EnablePerformanceTelemetry(telemetry),
            "Telemetry cannot be configured twice");
        CheckThrows<ArgumentOutOfRangeException>(
            () => _ = new PerformanceBudget(maxDrawCalls: -1),
            "Negative performance limits are rejected");

        var frame = new FrameStatisticsSnapshot(1, 60, 60, 12, 6, 3, 7);
        var memory = new GpuMemoryEstimate(
            2, 100,
            1, 200,
            3, 300,
            1, 50,
            1, 25);
        var budget = new PerformanceBudget(
            maxDrawCalls: 11,
            maxBatchFlushes: 6,
            maxTextureSwitches: 2,
            maxActivePasses: 8,
            maxEstimatedGpuMemoryBytes: 674);
        var violations = budget.Evaluate(frame, memory);
        Check(violations.Select(item => item.Metric).SequenceEqual(new[]
              {
                  PerformanceMetric.DrawCalls,
                  PerformanceMetric.TextureSwitches,
                  PerformanceMetric.EstimatedGpuMemoryBytes
              }),
            "Budgets report only strictly exceeded frame and memory limits");

        Check(RenderTargetMemoryEstimator.EstimateBytes(
                  new RenderTargetDescriptor(10, 20)) == 800 &&
              RenderTargetMemoryEstimator.EstimateBytes(
                  new RenderTargetDescriptor(
                      10, 20,
                      RenderTargetColorFormat.Rgba16Float,
                      RenderTargetDepthStencilFormat.Depth24Stencil8)) == 2400,
            "RenderTarget estimates include declared color and depth/stencil formats");

        long timestamp = 0;
        int captures = 0;
        var sampler = new PerformanceTelemetrySampler(
            telemetry,
            () =>
            {
                captures++;
                return new RuntimePerformanceSnapshot(
                    DateTimeOffset.UnixEpoch,
                    frame,
                    null!,
                    memory,
                    Array.Empty<CustomGpuMemoryDiagnostics>(),
                    violations);
            },
            () => timestamp,
            timestampFrequency: 1000);
        Check(sampler.Tick() && captures == 1 && sink.Snapshots.Count == 1,
            "First completed frame publishes immediately");
        timestamp = 999;
        Check(!sampler.Tick() && captures == 1,
            "Frames inside the interval do not capture or publish");
        timestamp = 1000;
        Check(sampler.Tick() && captures == 2 && sink.Snapshots.Count == 2,
            "The next interval publishes one fresh snapshot");
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

    private sealed class Probe(
        string name,
        List<string> order,
        bool fail = false) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            order.Add(name);
            if (fail) throw new InvalidOperationException(name);
        }
    }

    private sealed class RecordingTelemetrySink : IPerformanceTelemetrySink
    {
        public List<RuntimePerformanceSnapshot> Snapshots { get; } = new();

        public void Publish(RuntimePerformanceSnapshot snapshot) => Snapshots.Add(snapshot);
    }
}
