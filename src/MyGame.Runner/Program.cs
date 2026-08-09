namespace MyGame.Runner;

using System.Numerics;
using System.Text;
using System.Text.Json;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Hosting;
using MyGame.Runner.Content;

internal static class Program {
    private const string MainOnlyLayer = "MainOnly";

    private static void Main( string[] args ) {
        bool smoke = args.Contains( "--smoke", StringComparer.Ordinal );
        bool consoleDiagnostics = args.Contains( "--diagnostics", StringComparer.Ordinal );
        bool contentHotReload = args.Contains( "--content-hot-reload", StringComparer.Ordinal );
        bool shaderHotReload = args.Contains( "--shader-hot-reload", StringComparer.Ordinal );
        bool mirroredViewports = args.Contains( "--mirrored-viewports", StringComparer.Ordinal );
        bool splitCameras = args.Contains( "--split-cameras", StringComparer.Ordinal );
        if ( mirroredViewports && splitCameras )
            throw new ArgumentException( "Choose either --mirrored-viewports or --split-cameras." );
        string? diagnosticsJson = GetOptionValue( args, "--diagnostics-json" );
        using var telemetrySink = consoleDiagnostics || diagnosticsJson is not null ||
                                  contentHotReload || shaderHotReload
            ? new RunnerPerformanceTelemetrySink(
                consoleDiagnostics || contentHotReload || shaderHotReload,
                diagnosticsJson )
            : null;
        Console.WriteLine( "=== Engine Hosting Demo ===" );
        Console.WriteLine( "  4 个 OrbitingSprite 做圆周运动" );
        Console.WriteLine( "  鼠标位置 = Spotlight 中心 (Stencil ShowInside)" );
        Console.WriteLine( "  主 View: HDR Scene → Bloom → ACES Tone Mapping" );
        Console.WriteLine( "  ESC: 退出" );
        if ( mirroredViewports ) Console.WriteLine( "  Single Camera → two Cover Viewports" );
        if ( splitCameras )
            Console.WriteLine( "  Two Cameras → main HDR left / lightweight LDR observer right" );

        var windowOptions = smoke
            ? EngineWindowOptions.Default
                .WithFrameRate(new FrameRateSettings(60, 60, vSync: false))
                .WithFrameStatistics(new FrameStatisticsOptions(0.25d)) with {
                    IsVisible = false,
                    FixedDeltaTime = 1d / 60d
                }
            : EngineWindowOptions.Default;

        using var game = GameApplication
            .Create( windowOptions )
            .UseDefault2DRenderer( renderer => ConfigureRenderer(
                renderer,
                telemetrySink,
                consoleDiagnostics || diagnosticsJson is not null,
                contentHotReload,
                shaderHotReload,
                mirroredViewports,
                splitCameras ) )
            .ConfigureScene( "MainScene", context => ConfigureScene( context, smoke, splitCameras ) )
            .Build();

        game.Run();
    }

    private static void ConfigureRenderer(
        Default2DRendererOptions renderer,
        RunnerPerformanceTelemetrySink? telemetrySink,
        bool performanceTelemetry,
        bool contentHotReload,
        bool shaderHotReload,
        bool mirroredViewports,
        bool splitCameras ) {
        renderer
            .UseContent( GameAssets.Packages.Root )
            .UseShaderAssets( GameShaders.ManifestPath )
            .UseHdr(
                ToneMappingSettings.Default,
                new BloomSettings(
                    0.3f,
                    1.5f,
                    1f,
                    2,
                    BloomResolution.Half ) )
            .EnableStencilMasking();
        if ( splitCameras ) {
            renderer.UseRenderViews( views => views
                .ConfigureMain( ViewportRect.LeftHalf )
                .Add(
                    "observer",
                    ViewportRect.RightHalf,
                    renderScale: 0.75f,
                    sceneLayers: SceneLayerFilter.Exclude( MainOnlyLayer ) ) );
        }
        if ( mirroredViewports ) {
            renderer.UseSingleCameraViewports( views => views
                .Add( "left", ViewportRect.LeftHalf, ViewportFitMode.Cover )
                .Add( "right", ViewportRect.RightHalf, ViewportFitMode.Cover ) );
        }
        if ( performanceTelemetry ) {
            renderer.EnablePerformanceTelemetry( new PerformanceTelemetryOptions(
                telemetrySink!,
                TimeSpan.FromSeconds( 1 ),
                new PerformanceBudget(
                    maxDrawCalls: 500,
                    maxBatchFlushes: 250,
                    maxTextureSwitches: 100,
                    maxActivePasses: 32,
                    maxEstimatedGpuMemoryBytes: 256L * 1024 * 1024 ) ) );
        }
        if ( contentHotReload ) {
            renderer.EnableContentHotReload( new ContentHotReloadOptions(
                telemetrySink!,
                TimeSpan.FromMilliseconds( 250 ),
                TimeSpan.FromMilliseconds( 250 ) ) );
        }
        if ( shaderHotReload ) {
            renderer.EnableShaderHotReload( new ShaderHotReloadOptions(
                telemetrySink!,
                TimeSpan.FromMilliseconds( 250 ),
                TimeSpan.FromMilliseconds( 250 ) ) );
        }
    }

    private static void ConfigureScene(
        Default2DGameContext context,
        bool smoke,
        bool splitCameras ) {
        var scene = context.Scene;
        scene.Background = BackgroundConfig.FromColor(
            new Vector4( 0.08f, 0.10f, 0.13f, 1f ) );
        scene.OnStart = () => Console.WriteLine( $"[Scene] '{scene.SceneName}' started." );

        var orbitingSprite = GameAssets.Sprites.RunnerOrbiting;
        var orbitMaterial = GameShaders.Materials.RunnerOrbitMaterial;
        context.Shaders.Set( GameShaders.Parameters.RunnerOrbitMaterial.Gain, 1f );
        var center = new Vector2D(
            context.Window.Width * 0.5f,
            context.Window.Height * 0.5f );
        if ( splitCameras ) {
            ConfigureCameraAround( context.GetRenderView( RenderViewRef.Main ), center, 1f );
            ConfigureCameraAround(
                context.GetRenderView( new RenderViewRef( "observer" ) ),
                center,
                0.65f );
        }
        var colors = new[] {
            new Vector4( 1.0f, 0.3f, 0.3f, 1.0f ), new Vector4( 0.3f, 1.0f, 0.3f, 1.0f ), new Vector4( 0.3f, 0.5f, 1.0f, 1.0f ),
            new Vector4( 1.0f, 1.0f, 0.3f, 1.0f )
        };
        if ( splitCameras ) scene.AddLayer( MainOnlyLayer, -100 );
        for (int i = 0; i < colors.Length; i++) {
            var sprite = new OrbitingSprite(
                center,
                200f,
                i * MathF.PI / 2f,
                colors[i],
                orbitingSprite,
                orbitMaterial );
            if ( splitCameras && i == 0 ) sprite.LayerName = MainOnlyLayer;
            scene.Add( sprite );
        }

        var spotlightGroup = new StencilMaskGroupRef( "spotlight" );
        scene.Add( new SpotlightController(
            spotlightGroup,
            scene.RaiseEvent,
            context,
            center,
            120f,
            context.Close ) );
        if ( splitCameras ) {
            context.PresentViewSurface(
                RenderViewRef.Main,
                spotlightGroup.Output,
                layer: 100,
                blend: GameEngine.Features.Presentation.Domain.PresentationBlendMode.AlphaBlend );
        }
        else {
            context.PresentWorldSurface(
                spotlightGroup.Output,
                layer: 100,
                blend: GameEngine.Features.Presentation.Domain.PresentationBlendMode.AlphaBlend );
        }
        if ( smoke ) scene.Add( new SmokeExitController( context, context.Close ) );
    }

    private static void ConfigureCameraAround(
        RenderView view,
        Vector2D center,
        float zoom ) {
        view.Camera.Zoom = zoom;
        view.Camera.Position = new Vector2(
            (float)center.X - (float)view.RenderSize.X / (2f * zoom),
            (float)center.Y - (float)view.RenderSize.Y / (2f * zoom) );
    }

    private sealed class SmokeExitController(
        Default2DGameContext context,
        Action close ) : GameInstance {
        private int _steps;

        public override void OnStep( double deltaTime ) {
            if ( _steps == 1 ) {
                var diagnostics = context.CaptureRenderDiagnostics();
                if ( context.RenderViews.Count > 1 ) {
                    var leftPoint = new Vector2D(
                        context.Window.Width * 0.25f,
                        context.Window.Height * 0.5f );
                    var rightPoint = new Vector2D(
                        context.Window.Width * 0.75f,
                        context.Window.Height * 0.5f );
                    if ( !context.TryScreenToView( leftPoint, out ViewportHit left ) ||
                         left.View != RenderViewRef.Main ||
                         !context.TryScreenToView( rightPoint, out ViewportHit right ) ||
                         right.View != new RenderViewRef( "observer" ) ||
                         diagnostics.Viewports[1].RenderWidth >= diagnostics.Viewports[0].RenderWidth ||
                         diagnostics.Viewports[0].SceneLayers.IsAll == false ||
                         diagnostics.Viewports[1].SceneLayers.Allows( MainOnlyLayer ) ) {
                        throw new InvalidOperationException(
                            "Multi-Camera Viewport mapping or RenderScale diagnostics are invalid." );
                    }
                    ViewportSlotDiagnostics main = diagnostics.Viewports[0];
                    if ( diagnostics.RenderTargets.ActiveLeases.Count == 0 )
                        throw new InvalidOperationException(
                            "Primary View effects did not rent their expected targets." );
                    int maxLeaseWidth = diagnostics.RenderTargets.ActiveLeases
                        .Max( lease => lease.Descriptor.Width );
                    int maxLeaseHeight = diagnostics.RenderTargets.ActiveLeases
                        .Max( lease => lease.Descriptor.Height );
                    if ( diagnostics.Effects.Width != main.RenderWidth ||
                         diagnostics.Effects.Height != main.RenderHeight ||
                         maxLeaseWidth != main.RenderWidth ||
                         maxLeaseHeight != main.RenderHeight ) {
                        throw new InvalidOperationException(
                            "Primary View effects were not built at the primary render resolution." );
                    }
                }
                if ( diagnostics.Pipeline.DependencyError is not null ||
                     diagnostics.Pipeline.Passes.Count == 0 ||
                     diagnostics.Effects.Effects.Count == 0 ||
                     diagnostics.Effects.Surfaces.Count == 0 ||
                     diagnostics.RenderTargets.ActiveLeases.Count == 0 ||
                     diagnostics.Viewports.Count == 0 ||
                     ( diagnostics.Viewports.Count != context.RenderViews.Count &&
                       context.RenderViews.Count > 1 ) ||
                     diagnostics.FrameStatistics is not { DrawCalls: > 0, BatchFlushes: > 0, ActivePasses: > 0 } ) {
                    throw new InvalidOperationException(
                        "Hosting render diagnostics did not capture the active runtime graph." );
                }
                Console.WriteLine(
                    $"[Diagnostics] passes={diagnostics.Pipeline.Passes.Count}, " +
                    $"effects={diagnostics.Effects.Effects.Count}, " +
                    $"views={context.RenderViews.Count}, " +
                    $"surfaces={diagnostics.Effects.Surfaces.Count}, " +
                    $"leases={diagnostics.RenderTargets.ActiveLeases.Count}, " +
                    $"drawCalls={diagnostics.FrameStatistics.Value.DrawCalls}, " +
                    $"flushes={diagnostics.FrameStatistics.Value.BatchFlushes}, " +
                    $"textureSwitches={diagnostics.FrameStatistics.Value.TextureSwitches}, " +
                    $"activePasses={diagnostics.FrameStatistics.Value.ActivePasses}" );
                var performance = context.CapturePerformanceSnapshot();
                if ( performance.GpuMemory.TextureCount == 0 ||
                     performance.GpuMemory.RootRenderTargetCount != context.RenderViews.Count + 1 ||
                     performance.GpuMemory.LeasedRenderTargetCount == 0 ||
                     performance.GpuMemory.TotalBytes <= 0 ) {
                    throw new InvalidOperationException(
                        "Hosting performance diagnostics did not capture GPU resource estimates." );
                }
            }
            if ( ++_steps >= 3 ) close();
        }
    }

    private static string? GetOptionValue( string[] args, string option ) {
        for (int i = 0; i < args.Length; i++) {
            if ( args[i].StartsWith( option + "=", StringComparison.Ordinal ) )
                return RequireOptionValue( args[i][(option.Length + 1)..], option );
            if ( args[i] != option ) continue;
            if ( i + 1 >= args.Length || args[i + 1].StartsWith( "--", StringComparison.Ordinal ) )
                throw new ArgumentException( $"{option} requires a file path." );
            return RequireOptionValue( args[i + 1], option );
        }
        return null;
    }

    private static string RequireOptionValue( string value, string option ) =>
        string.IsNullOrWhiteSpace( value )
            ? throw new ArgumentException( $"{option} requires a file path." )
            : value;

    private sealed class RunnerPerformanceTelemetrySink :
        IPerformanceTelemetrySink,
        IContentHotReloadSink,
        IShaderHotReloadSink,
        IDisposable {
        private readonly bool _writeConsole;
        private readonly StreamWriter? _jsonWriter;

        public RunnerPerformanceTelemetrySink( bool writeConsole, string? jsonPath ) {
            _writeConsole = writeConsole;
            if ( jsonPath is null ) return;
            string fullPath = Path.GetFullPath( jsonPath );
            string? directory = Path.GetDirectoryName( fullPath );
            if ( !string.IsNullOrEmpty( directory ) ) Directory.CreateDirectory( directory );
            _jsonWriter = new StreamWriter( fullPath, append: false, new UTF8Encoding( false ) );
        }

        public void Publish( RuntimePerformanceSnapshot snapshot ) {
            if ( _writeConsole ) {
                var frame = snapshot.Frame;
                Console.WriteLine(
                    $"[Performance] fps={frame?.FramesPerSecond:F1}, " +
                    $"ups={frame?.UpdatesPerSecond:F1}, " +
                    $"draw={frame?.DrawCalls}, flush={frame?.BatchFlushes}, " +
                    $"queries={snapshot.GameplayQueries.TotalQueries}, " +
                    $"queryMs/step={snapshot.GameplayQueries.AverageMillisecondsPerStep:F4}, " +
                    $"textures={snapshot.GpuMemory.TextureCount}, " +
                    $"gpuMiB={snapshot.GpuMemory.TotalBytes / 1048576d:F2}, " +
                    $"violations={snapshot.BudgetViolations.Count}" );
            }
            if ( _jsonWriter is null ) return;
            _jsonWriter.WriteLine( JsonSerializer.Serialize( snapshot ) );
            _jsonWriter.Flush();
        }

        public void Publish( ContentHotReloadDiagnostic diagnostic ) {
            if ( _writeConsole ) {
                string suffix = diagnostic.Error is null ? string.Empty : $", error={diagnostic.Error}";
                Console.WriteLine(
                    $"[ContentHotReload] status={diagnostic.Status}, " +
                    $"package={diagnostic.PackageId}, revision={diagnostic.Fingerprint}, " +
                    $"durationMs={diagnostic.Duration.TotalMilliseconds:F1}{suffix}" );
            }
            if ( _jsonWriter is null ) return;
            _jsonWriter.WriteLine( JsonSerializer.Serialize( diagnostic ) );
            _jsonWriter.Flush();
        }

        public void Publish( ShaderHotReloadDiagnostic diagnostic ) {
            if ( _writeConsole ) {
                string suffix = diagnostic.Error is null ? string.Empty : $", error={diagnostic.Error}";
                Console.WriteLine(
                    $"[ShaderHotReload] status={diagnostic.Status}, " +
                    $"shaders={string.Join( ',', diagnostic.ShaderNames )}, " +
                    $"revision={diagnostic.Fingerprint}, " +
                    $"durationMs={diagnostic.Duration.TotalMilliseconds:F1}{suffix}" );
            }
            if ( _jsonWriter is null ) return;
            _jsonWriter.WriteLine( JsonSerializer.Serialize( diagnostic ) );
            _jsonWriter.Flush();
        }

        public void Dispose() => _jsonWriter?.Dispose();
    }
}
