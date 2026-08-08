namespace GameEngine.VisualRegressionTests;

using GameEngine.Testing.Visual;

internal static class Program {
    private static int Main( string[] args ) {
        try {
            var options = CommandLineOptions.Parse( args );
            RunComparerSelfChecks();

            string repositoryRoot = FindRepositoryRoot();
            string baselineRoot = Path.Combine(
                repositoryRoot, "src", "Engine.VisualRegressionTests", "Baselines" );
            string artifactRoot = Path.Combine(
                repositoryRoot, "artifacts", "visual-regression" );
            var scenarios = CreateScenarios()
                .Where( scenario => options.Scenario is null ||
                                    string.Equals( scenario.Name, options.Scenario, StringComparison.Ordinal ) )
                .ToArray();

            if ( scenarios.Length == 0 )
                throw new ArgumentException( $"Unknown scenario '{options.Scenario}'." );

            bool passed = true;
            foreach ( var scenario in scenarios ) {
                var captures = VisualRegressionHost.Run(
                    scenario,
                    new VisualRegressionHostOptions( options.Visible ) );
                foreach ( var result in VisualBaselineVerifier.Process(
                             captures, baselineRoot, artifactRoot, options.UpdateBaselines ) ) {
                    string state = options.UpdateBaselines
                        ? "UPDATED"
                        : result.Passed
                            ? "PASS"
                            : "FAIL";
                    Console.WriteLine( $"[{state}] {result.CaptureId}: {result.Message}" );
                    passed &= result.Passed || options.UpdateBaselines;
                }
            }

            return passed ? 0 : 1;
        }
        catch (VisualGraphicsUnavailableException exception) {
            Console.Error.WriteLine( $"[SKIP] {exception.Message}" );
            Console.Error.WriteLine( exception.InnerException?.Message );
            return 2;
        }
        catch (Exception exception) {
            Console.Error.WriteLine( exception );
            return 1;
        }
    }

    private static IEnumerable<IVisualRegressionScenario> CreateScenarios() {
        yield return new SpriteOriginTransformScenario();
        yield return new StencilOwnerLifecycleScenario();
        yield return new DynamicEffectResizeScenario();
        yield return new BloomPingPongScenario();
        yield return new RenderSurfaceChainScenario();
        yield return new HdrToneMappingScenario();
    }

    private static void RunComparerSelfChecks() {
        var transparentA = new CapturedFrame( 1, 1, new byte[] {
            255, 0, 0, 0
        } );
        var transparentB = new CapturedFrame( 1, 1, new byte[] {
            0, 255, 255, 0
        } );
        Require( PixelComparer.Compare( transparentA, transparentB ).IsMatch,
            "Transparent RGB must be ignored." );

        var expected = new CapturedFrame( 1, 1, new byte[] {
            20, 30, 40, 255
        } );
        var tolerated = new CapturedFrame( 1, 1, new byte[] {
            22, 29, 41, 255
        } );
        Require( PixelComparer.Compare( expected, tolerated ).IsMatch,
            "Soft-threshold differences must pass." );

        var changed = new CapturedFrame( 1, 1, new byte[] {
            40, 30, 40, 255
        } );
        Require( !PixelComparer.Compare( expected, changed ).IsMatch,
            "Hard-threshold differences must fail." );
    }

    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo( Environment.CurrentDirectory );
        while (directory is not null) {
            if ( File.Exists( Path.Combine( directory.FullName, "MyGameEngine.slnx" ) ) )
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException( "Could not locate MyGameEngine.slnx." );
    }

    private static void Require( bool condition, string message ) {
        if ( !condition ) throw new InvalidOperationException( message );
    }

    private sealed record CommandLineOptions(
        bool UpdateBaselines,
        bool Visible,
        string? Scenario ) {
        public static CommandLineOptions Parse( string[] args ) {
            bool update = false;
            bool visible = false;
            string? scenario = null;
            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--verify":
                        break;
                    case "--update-baselines":
                        update = true;
                        break;
                    case "--visible":
                        visible = true;
                        break;
                    case "--scenario" when i + 1 < args.Length:
                        scenario = args[++i];
                        break;
                    default:
                        throw new ArgumentException( $"Unknown or incomplete argument '{args[i]}'." );
                }
            }

            return new CommandLineOptions( update, visible, scenario );
        }
    }
}
