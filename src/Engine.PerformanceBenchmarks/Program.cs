namespace GameEngine.PerformanceBenchmarks;

using System.Diagnostics;
using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

internal static class Program
{
    private static readonly Bounds2D MainBounds = new(0, 0, 800, 600);
    private static readonly Bounds2D ObserverBounds = new(1_000, 0, 1_800, 600);
    private static readonly SceneLayerFilter ObserverLayers =
        SceneLayerFilter.Exclude("MainOnly");
    private static readonly SpriteRef BenchmarkSprite = new("benchmark.multi-view");

    private static int Main(string[] args)
    {
        try
        {
            BenchmarkOptions options = BenchmarkOptions.Parse(args);
            Console.WriteLine("=== Multi-View Scene Dispatch Benchmark ===");
            Console.WriteLine(
                $"Runtime={Environment.Version}, CPU={Environment.ProcessorCount}, " +
                $"warmup={options.WarmupFrames}, frames={options.MeasurementFrames}");
            Console.WriteLine(
                "Times are observations; deterministic counts and allocations are regression guards.\n");

            foreach (int count in new[] { 100, 1_000, 10_000 })
                Print(Measure(count, options));

            Console.WriteLine("\n=== Benchmark invariants passed ===");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("\n=== Performance benchmark failed ===");
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static MultiViewBenchmarkResult Measure(
        int instanceCount,
        BenchmarkOptions options)
    {
        SceneAggregate scene = BuildScene(instanceCount);
        var batch = new NullSpriteBatch();

        for (int i = 0; i < options.WarmupFrames; i++)
        {
            _ = DrawUnculled(scene, batch);
            _ = DrawCulled(scene, batch, out _, out _);
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long unculledTicks = 0;
        long culledTicks = 0;
        SceneDrawStatistics main = default;
        SceneDrawStatistics observer = default;

        for (int frame = 0; frame < options.MeasurementFrames; frame++)
        {
            if ((frame & 1) == 0)
            {
                unculledTicks += DrawUnculled(scene, batch);
                culledTicks += DrawCulled(scene, batch, out main, out observer);
            }
            else
            {
                culledTicks += DrawCulled(scene, batch, out main, out observer);
                unculledTicks += DrawUnculled(scene, batch);
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var result = new MultiViewBenchmarkResult(
            instanceCount,
            ToMillisecondsPerFrame(unculledTicks, options.MeasurementFrames),
            ToMillisecondsPerFrame(culledTicks, options.MeasurementFrames),
            allocated,
            main,
            observer);
        Validate(result);
        return result;
    }

    private static SceneAggregate BuildScene(int instanceCount)
    {
        var scene = new SceneAggregate($"MultiViewBenchmark-{instanceCount}");
        scene.AddLayer("Effects", 500);
        scene.AddLayer("Projectiles", -100);
        scene.AddLayer("MainOnly", -500);
        string[] layers =
        [
            SceneAggregate.LayerNameInstances,
            "Effects",
            "Projectiles",
            "MainOnly"
        ];

        for (int i = 0; i < instanceCount; i++)
        {
            int positionGroup = i % 5;
            Vector2D position = positionGroup switch
            {
                0 => new Vector2D(100 + i % 600, 100 + i % 400),
                1 => new Vector2D(1_100 + i % 600, 100 + i % 400),
                _ => new Vector2D(5_000 + i, 5_000)
            };
            scene.Add(new BenchmarkInstance(position, new LayerDepth(i % 8))
            {
                LayerName = layers[i % layers.Length],
                Sprite = BenchmarkSprite
            });
        }
        scene.MarkEventsAsCommitted();
        return scene;
    }

    private static long DrawUnculled(SceneAggregate scene, ISpriteBatch batch)
    {
        long started = Stopwatch.GetTimestamp();
        _ = scene.DrawActiveMeasured(batch, SceneLayerFilter.All, measureTime: false);
        _ = scene.DrawActiveMeasured(batch, ObserverLayers, measureTime: false);
        return Stopwatch.GetTimestamp() - started;
    }

    private static long DrawCulled(
        SceneAggregate scene,
        ISpriteBatch batch,
        out SceneDrawStatistics main,
        out SceneDrawStatistics observer)
    {
        long started = Stopwatch.GetTimestamp();
        main = scene.DrawActiveMeasured(
            batch, SceneLayerFilter.All, MainBounds, measureTime: false);
        observer = scene.DrawActiveMeasured(
            batch, ObserverLayers, ObserverBounds, measureTime: false);
        return Stopwatch.GetTimestamp() - started;
    }

    private static double ToMillisecondsPerFrame(long ticks, int frames) =>
        ticks * 1_000d / Stopwatch.Frequency / frames;

    private static void Validate(in MultiViewBenchmarkResult result)
    {
        int expectedMainCandidates = result.InstanceCount;
        int expectedObserverCandidates = result.InstanceCount * 3 / 4;
        int expectedMainDrawn = result.InstanceCount / 5;
        int expectedObserverDrawn = result.InstanceCount * 3 / 20;

        Require(result.Main.CandidateVisitCount == expectedMainCandidates,
            "Main candidate count changed.");
        Require(result.Observer.CandidateVisitCount == expectedObserverCandidates,
            "Observer layer-filtered candidate count changed.");
        Require(result.Main.DrawnInstanceCount == expectedMainDrawn,
            "Main visible draw count changed.");
        Require(result.Observer.DrawnInstanceCount == expectedObserverDrawn,
            "Observer visible draw count changed.");
        Require(result.Main.CulledInstanceCount == expectedMainCandidates - expectedMainDrawn,
            "Main culling count changed.");
        Require(
            result.Observer.CulledInstanceCount ==
            expectedObserverCandidates - expectedObserverDrawn,
            "Observer culling count changed.");
        Require(result.Main.SortComparisonCount == 0 &&
                result.Observer.SortComparisonCount == 0,
            "Per-View draw unexpectedly sorted instances.");
        Require(result.AllocatedBytes == 0,
            $"Steady-state drawing allocated {result.AllocatedBytes:N0} B in the sample.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Print(in MultiViewBenchmarkResult result)
    {
        Console.WriteLine(
            $"{result.InstanceCount,6:N0} instances | " +
            $"unculled={result.UnculledMilliseconds,8:F4} ms | " +
            $"culled={result.CulledMilliseconds,8:F4} ms | " +
            $"candidates={result.Main.CandidateVisitCount:N0}/" +
            $"{result.Observer.CandidateVisitCount:N0} | " +
            $"drawn={result.Main.DrawnInstanceCount:N0}/" +
            $"{result.Observer.DrawnInstanceCount:N0} | " +
            $"rejected={result.Main.CulledInstanceCount:N0}/" +
            $"{result.Observer.CulledInstanceCount:N0} | " +
            $"allocated={result.AllocatedBytes:N0} B total");
    }

    private readonly record struct MultiViewBenchmarkResult(
        int InstanceCount,
        double UnculledMilliseconds,
        double CulledMilliseconds,
        long AllocatedBytes,
        SceneDrawStatistics Main,
        SceneDrawStatistics Observer);

    private readonly record struct BenchmarkOptions(int WarmupFrames, int MeasurementFrames)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            int warmup = 128;
            int frames = 1_000;
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (option is "--warmup" or "--frames")
                {
                    if (++i >= args.Length || !int.TryParse(args[i], out int value) || value <= 0)
                        throw new ArgumentException($"{option} requires a positive integer.");
                    if (option == "--warmup") warmup = value;
                    else frames = value;
                    continue;
                }
                throw new ArgumentException($"Unknown option '{option}'.");
            }
            return new BenchmarkOptions(warmup, frames);
        }
    }

    private sealed class BenchmarkInstance(Vector2D position, LayerDepth depth)
        : GameInstance(nameof(BenchmarkInstance), position, depth)
    {
        public override void OnDraw(ISpriteBatch batch) { }
    }

    private sealed class NullSpriteBatch : ISpriteBatch
    {
        public void Begin() { }
        public void End() { }
        public void Flush() { }
        public void Draw(
            uint textureHandle,
            Vector2 position,
            Vector2 size,
            Vector4 color,
            Vector4 uvBounds) { }
        public void DrawSpriteCommand(in SpriteDrawCommand command) { }
        public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata)
        {
            metadata = new SpriteMetadata(new Vector2(16), new Vector2(8), 1, 0f);
            return !sprite.IsEmpty;
        }
        public void SetBlendMode(BlendMode mode) { }
        public void SetDepthState(bool depthTest, bool depthWrite) { }
        public void SetShader(ShaderRef? shader) { }
        public void SetMaterial(MaterialRef? material) { }
    }
}
