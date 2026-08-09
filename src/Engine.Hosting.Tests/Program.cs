namespace GameEngine.Hosting.Tests;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using SkiaSharp;

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
        TestContentHotReloadOptions();
        TestContentHotReloadCoordinator();
        TestShaderHotReloadConfiguration();
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

    private static void TestContentHotReloadOptions()
    {
        Console.WriteLine("6. Content hot reload configuration boundary");
        var sink = new RecordingHotReloadSink();
        var options = new ContentHotReloadOptions(
            sink,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200));
        var package = new ContentPackageRef("game.assets", "game/assets.json");
        var plan = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(package)
                .EnableContentHotReload(options))
            .ConfigureScene("HotReload", _ => { })
            .BuildPlan();
        Check(plan.Renderer.ContentHotReload == options,
            "Hot reload options are frozen into the renderer plan");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .EnableContentHotReload(options)
                .ToPlan()
                .Validate(),
            "Hot reload requires an explicitly configured content package");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .UseContent(package)
                .EnableContentHotReload(options)
                .EnableContentHotReload(options),
            "Hot reload cannot be configured twice");
        CheckThrows<ArgumentOutOfRangeException>(
            () => _ = new ContentHotReloadOptions(sink, TimeSpan.Zero),
            "Hot reload polling interval must be positive");
    }

    private static void TestContentHotReloadCoordinator()
    {
        Console.WriteLine("7. Content revision debounce, apply, and failure fallback");
        string root = Directory.CreateTempSubdirectory("mygame-hosting-reload-").FullName;
        try
        {
            string imagePath = Path.Combine(root, "live.webp");
            WriteWebp(imagePath, 2, SKColors.Red);
            WriteContentManifest(root);
            WriteContentRevision(root, "revision-1");

            var backend = new HotReloadTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, root);
            var packageRef = new ContentPackageRef("hosting.reload", "assets.json");
            using var package = manager.Load(packageRef);
            var sink = new RecordingHotReloadSink();
            var time = new ManualTimeProvider();
            using var coordinator = new ContentHotReloadCoordinator(
                manager,
                packageRef,
                new ContentHotReloadOptions(
                    sink,
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(20)),
                time);

            WriteWebp(imagePath, 4, SKColors.Blue);
            WriteContentRevision(root, "revision-2");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            Check(sink.Diagnostics.Select(item => item.Status)
                    .SequenceEqual(new[] { ContentHotReloadStatus.Detected }),
                "A changed stable fingerprint is detected before preparation");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            Check(sink.Diagnostics.Count == 1,
                "Debounce prevents an early revision preparation");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            SpinUntilTerminal(coordinator, sink, ContentHotReloadStatus.Applied);
            textures.TryGetMetadata(package.GetTexture("hosting.texture"), out var applied);
            Check(applied.Width == 4 && sink.Diagnostics[^1].Status == ContentHotReloadStatus.Applied,
                "A prepared revision commits at a later frame boundary");

            File.WriteAllBytes(imagePath, [1, 2, 3]);
            WriteContentRevision(root, "revision-bad");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            time.Advance(TimeSpan.FromMilliseconds(20));
            coordinator.Tick();
            SpinUntilTerminal(coordinator, sink, ContentHotReloadStatus.Failed);
            int failures = sink.Diagnostics.Count(item => item.Status == ContentHotReloadStatus.Failed);
            textures.TryGetMetadata(package.GetTexture("hosting.texture"), out var afterFailure);
            time.Advance(TimeSpan.FromSeconds(1));
            coordinator.Tick();
            Check(afterFailure.Width == 4 &&
                  sink.Diagnostics.Count(item => item.Status == ContentHotReloadStatus.Failed) == failures,
                "A failed fingerprint keeps the old resource and is not retried every poll");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestShaderHotReloadConfiguration()
    {
        Console.WriteLine("8. Shader file snapshots and hot reload configuration");
        string root = Directory.CreateTempSubdirectory("mygame-shader-reload-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "sprite.vert"), "vertex-v1");
            File.WriteAllText(Path.Combine(root, "sprite.frag"), "fragment-v1");
            string assetManifest = Path.Combine(root, "shaders.json");
            File.WriteAllText(assetManifest,
                """
                {
                  "schemaVersion":1,
                  "shaders":[
                    {"name":"game.sprite","vertex":"sprite.vert","fragment":"sprite.frag"}
                  ],
                  "materials":[
                    {
                      "name":"game.sprite.material",
                      "shader":"game.sprite",
                      "uniforms":[
                        {"name":"uGain","type":"float","default":1.5}
                      ]
                    }
                  ]
                }
                """);
            var definition = new ShaderFileDefinition(
                "game.sprite",
                "sprite.vert",
                "sprite.frag");
            ShaderFileSetSnapshot first = ShaderFileSetReader.Read(root, new[] { definition });
            File.WriteAllText(Path.Combine(root, "sprite.frag"), "fragment-v2");
            ShaderFileSetSnapshot second = ShaderFileSetReader.Read(root, new[] { definition });
            Check(first.Fingerprint != second.Fingerprint &&
                  second.ChangedNamesFrom(first).SequenceEqual(new[] { "game.sprite" }),
                "Source content hashes identify the exact changed Shader program");
            Check(second.Sources.Single().VertexPath == Path.Combine(root, "sprite.vert") &&
                  second.Sources.Single().FragmentPath == Path.Combine(root, "sprite.frag"),
                "Stable snapshots retain exact source paths for driver diagnostics");

            var buildError = new ShaderBuildException(
                "game.sprite",
                "FragmentShader",
                "ERROR: 0:17: unexpected token",
                Path.Combine(root, "sprite.frag"));
            Check(buildError.SourceLine == 17 &&
                  buildError.Message.Contains("sprite.frag':17", StringComparison.Ordinal),
                "Driver logs are enriched with the source path and parsed line number");

            var sink = new RecordingShaderHotReloadSink();
            var options = new ShaderHotReloadOptions(
                sink,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200));
            var plan = GameApplication.Create()
                .UseDefault2DRenderer(renderer => renderer
                    .UseShaders(root, definition)
                    .EnableShaderHotReload(options))
                .ConfigureScene("Shaders", _ => { })
                .BuildPlan();
            Check(plan.Renderer.ShaderRoot == root &&
                  plan.Renderer.ShaderFiles?.Single() == definition &&
                  plan.Renderer.ShaderHotReload == options,
                "Shader files and hot reload policy are frozen into the renderer plan");

            var assetPlan = GameApplication.Create()
                .UseDefault2DRenderer(renderer => renderer.UseShaderAssets(assetManifest))
                .ConfigureScene("ShaderAssets", _ => { })
                .BuildPlan();
            var declaredMaterial = assetPlan.Renderer.ShaderMaterials!.Single();
            Check(assetPlan.Renderer.ShaderAssetManifestPath == assetManifest &&
                  assetPlan.Renderer.ShaderRoot == root &&
                  assetPlan.Renderer.ShaderFiles?.Single().Name == "game.sprite" &&
                  declaredMaterial.Name == "game.sprite.material" &&
                  declaredMaterial.Uniforms.Single().DefaultValue.FloatValue == 1.5f,
                "Declarative Shader assets freeze programs, Material schema, and defaults");

            CheckThrows<InvalidOperationException>(
                () => new Default2DRendererOptions()
                    .EnableShaderHotReload(options)
                    .ToPlan()
                    .Validate(),
                "Shader hot reload requires registered file-backed Shaders");
            CheckThrows<ArgumentException>(
                () => new Default2DRendererOptions().UseShaders(
                    root,
                    definition,
                    definition),
                "Duplicate logical Shader names are rejected before GL initialization");
            CheckThrows<InvalidOperationException>(
                () => new Default2DRendererOptions()
                    .UseShaders(root, definition)
                    .UseShaderAssets(assetManifest),
                "Imperative and declarative Shader registration cannot overlap");
            CheckThrows<InvalidDataException>(
                () => ShaderFileSetReader.Read(root, new[]
                {
                    new ShaderFileDefinition("escape", "../outside.vert", "sprite.frag")
                }),
                "Shader source paths cannot escape their configured root");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SpinUntilTerminal(
        ContentHotReloadCoordinator coordinator,
        RecordingHotReloadSink sink,
        ContentHotReloadStatus terminal)
    {
        for (int i = 0; i < 200; i++)
        {
            coordinator.Tick();
            if (sink.Diagnostics.Any(item => item.Status == terminal)) return;
            Thread.Sleep(2);
        }
        throw new TimeoutException($"Content hot reload did not report {terminal}.");
    }

    private static void WriteContentManifest(string root) => File.WriteAllText(
        Path.Combine(root, "assets.json"),
        """
        { "schemaVersion":1, "id":"hosting.reload", "dependencies":[],
          "textures":[{"name":"hosting.texture","path":"live.webp"}],
          "sprites":[{"name":"hosting.sprite","layout":"single","texture":"hosting.texture",
            "origin":{"x":0,"y":0}}] }
        """);

    private static void WriteContentRevision(string root, string fingerprint) => File.WriteAllText(
        Path.Combine(root, CompiledContentRevisionReader.MetadataFileName),
        $$"""
        { "schemaVersion":1, "owner":"MyGameEngine.AssetCompiler", "compilerVersion":"2",
          "rootPackageId":"hosting.reload", "rootManifest":"assets.json",
          "inputFingerprint":"{{fingerprint}}" }
        """);

    private static void WriteWebp(string path, int size, SKColor color)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 100)
            ?? throw new InvalidOperationException("Could not encode WebP fixture.");
        File.WriteAllBytes(path, data.ToArray());
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

    private sealed class RecordingHotReloadSink : IContentHotReloadSink
    {
        public List<ContentHotReloadDiagnostic> Diagnostics { get; } = [];

        public void Publish(ContentHotReloadDiagnostic diagnostic) => Diagnostics.Add(diagnostic);
    }

    private sealed class RecordingShaderHotReloadSink : IShaderHotReloadSink
    {
        public List<ShaderHotReloadDiagnostic> Diagnostics { get; } = [];

        public void Publish(ShaderHotReloadDiagnostic diagnostic) => Diagnostics.Add(diagnostic);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class HotReloadTextureBackend : ITextureBackend
    {
        private uint _next = 1;

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler) => _next++;

        public void DeleteTexture(uint handle)
        {
        }
    }
}
