namespace MyGame.Runner;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.StencilMasking.Domain;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Hosting;
using MyGame.Runner.Content;

internal static class Program {
    private static void Main( string[] args ) {
        bool smoke = args.Contains( "--smoke", StringComparer.Ordinal );
        Console.WriteLine( "=== Engine Hosting Demo ===" );
        Console.WriteLine( "  4 个 OrbitingSprite 做圆周运动" );
        Console.WriteLine( "  鼠标位置 = Spotlight 中心 (Stencil ShowInside)" );
        Console.WriteLine( "  HDR Scene → Bloom → ACES Tone Mapping 由 Hosting 默认预设装配" );
        Console.WriteLine( "  ESC: 退出" );

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
            .UseDefault2DRenderer( renderer => renderer
                .UseContent( GameAssets.Packages.Root )
                .UseHdr(
                    ToneMappingSettings.Default,
                    new BloomSettings(
                        0.3f,
                        1.5f,
                        1f,
                        2,
                        BloomResolution.Half ) )
                .EnableStencilMasking() )
            .ConfigureScene( "MainScene", context => ConfigureScene( context, smoke ) )
            .Build();

        game.Run();
    }

    private static void ConfigureScene( Default2DGameContext context, bool smoke ) {
        var scene = context.Scene;
        scene.Background = BackgroundConfig.FromColor(
            new Vector4( 0.08f, 0.10f, 0.13f, 1f ) );
        scene.OnStart = () => Console.WriteLine( $"[Scene] '{scene.SceneName}' started." );

        var orbitingSprite = GameAssets.Sprites.RunnerOrbiting;
        var center = new Vector2D(
            context.Window.Width * 0.5f,
            context.Window.Height * 0.5f );
        var colors = new[] {
            new Vector4( 1.0f, 0.3f, 0.3f, 1.0f ), new Vector4( 0.3f, 1.0f, 0.3f, 1.0f ), new Vector4( 0.3f, 0.5f, 1.0f, 1.0f ),
            new Vector4( 1.0f, 1.0f, 0.3f, 1.0f )
        };
        for (int i = 0; i < colors.Length; i++) {
            scene.Add( new OrbitingSprite(
                center,
                200f,
                i * MathF.PI / 2f,
                colors[i],
                orbitingSprite ) );
        }

        var spotlightGroup = new StencilMaskGroupRef( "spotlight" );
        scene.Add( new SpotlightController(
            spotlightGroup,
            scene.RaiseEvent,
            center,
            120f,
            context.Close ) );
        if ( smoke ) scene.Add( new SmokeExitController( context, context.Close ) );
    }

    private sealed class SmokeExitController(
        Default2DGameContext context,
        Action close ) : GameInstance {
        private int _steps;

        public override void OnStep( double deltaTime ) {
            if ( _steps == 1 ) {
                var diagnostics = context.CaptureRenderDiagnostics();
                if ( diagnostics.Pipeline.DependencyError is not null ||
                     diagnostics.Pipeline.Passes.Count == 0 ||
                     diagnostics.Effects.Effects.Count == 0 ||
                     diagnostics.Effects.Surfaces.Count == 0 ||
                     diagnostics.RenderTargets.ActiveLeases.Count == 0 ||
                     diagnostics.FrameStatistics is not { DrawCalls: > 0, BatchFlushes: > 0, ActivePasses: > 0 } ) {
                    throw new InvalidOperationException(
                        "Hosting render diagnostics did not capture the active runtime graph." );
                }
                Console.WriteLine(
                    $"[Diagnostics] passes={diagnostics.Pipeline.Passes.Count}, " +
                    $"effects={diagnostics.Effects.Effects.Count}, " +
                    $"surfaces={diagnostics.Effects.Surfaces.Count}, " +
                    $"leases={diagnostics.RenderTargets.ActiveLeases.Count}, " +
                    $"drawCalls={diagnostics.FrameStatistics.Value.DrawCalls}, " +
                    $"flushes={diagnostics.FrameStatistics.Value.BatchFlushes}, " +
                    $"textureSwitches={diagnostics.FrameStatistics.Value.TextureSwitches}, " +
                    $"activePasses={diagnostics.FrameStatistics.Value.ActivePasses}" );
            }
            if ( ++_steps >= 3 ) close();
        }
    }
}
