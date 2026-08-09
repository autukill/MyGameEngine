namespace AsteroidsPlayground;

using System.Numerics;
using AsteroidsPlayground.Content;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        bool diagnostics = args.Contains("--diagnostics", StringComparer.Ordinal);
        var queryTelemetry = diagnostics ? new QueryTelemetrySink() : null;
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "MyGameEngine Playground - Asteroids",
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        using var game = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAction(GameInputs.TurnLeft, InputKey.A, InputKey.Left)
                .BindAction(GameInputs.TurnRight, InputKey.D, InputKey.Right)
                .BindAction(GameInputs.Thrust, InputKey.W, InputKey.Up)
                .BindAction(GameInputs.Fire, InputKey.Space)
                .BindAction(GameInputs.Pause, InputKey.P)
                .BindAction(GameInputs.Restart, InputKey.Enter))
            .UseDefault2DRenderer(renderer =>
            {
                renderer.UseContent(GameAssets.Packages.Root);
                if (queryTelemetry is not null)
                {
                    renderer.EnablePerformanceTelemetry(new PerformanceTelemetryOptions(
                        queryTelemetry,
                        TimeSpan.FromSeconds(1)));
                }
            })
            .ConfigureInstances(instances =>
            {
                instances.Register(
                    PlayerShip.LaserPrefab,
                    (in LaserSpawnArgs spawn) =>
                        new Laser(GameAssets.Sprites.AsteroidsLaser, spawn));
                instances.Register(
                    AsteroidSpawner.AsteroidPrefab,
                    (in AsteroidSpawnArgs spawn) =>
                        new Asteroid(GameAssets.Sprites.AsteroidsRock, spawn));
            })
            .AddScene(GameScenes.Main, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.01f, 0.015f, 0.045f, 1f));
                var player = new PlayerShip(
                    GameAssets.Sprites.AsteroidsShip,
                    new Vector2D(context.Window.Width * 0.5f, context.Window.Height * 0.5f),
                    context.Window.Width,
                    context.Window.Height);
                context.Scene.Add(player);
                context.Scene.Add(new AsteroidSpawner(
                    player.ToInstanceRef(),
                    context.Window.Width,
                    context.Window.Height));
                context.Scene.Add(new PauseController());
                if (smoke)
                    context.Scene.Add(new SmokeJourney(
                        GameScenes.GameOver,
                        new GameOverArgs(3d / 60d, 0),
                        context.Close));
            })
            .AddScene(GameScenes.GameOver, (context, gameOver) =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.14f, 0.015f, 0.02f, 1f));
                context.Scene.Add(new GameOverMarker(
                    GameAssets.Sprites.AsteroidsShip,
                    new Vector2D(context.Window.Width * 0.5f, context.Window.Height * 0.5f),
                    gameOver));
            })
            .StartScene(GameScenes.Main)
            .Build();

        game.Run();
    }

    private sealed class QueryTelemetrySink : IPerformanceTelemetrySink
    {
        public void Publish(RuntimePerformanceSnapshot snapshot)
        {
            GameplayQueryStatisticsSnapshot queries = snapshot.GameplayQueries;
            Console.WriteLine(
                $"[Queries] steps={queries.SampledSteps}, calls={queries.TotalQueries}, " +
                $"candidates={queries.TotalCandidates}, hits={queries.TotalHits}, " +
                $"ms/step={queries.AverageMillisecondsPerStep:F4}");
        }
    }

    private sealed class SmokeJourney : GameInstance
    {
        private static readonly GameplayPauseKey SmokePause = new("asteroids.smoke-pause");
        private readonly SceneRef<GameOverArgs> _next;
        private readonly GameOverArgs _args;
        private readonly Action _close;
        private int _steps;

        public SmokeJourney(SceneRef<GameOverArgs> next, GameOverArgs args, Action close)
        {
            _next = next;
            _args = args;
            _close = close;
            IsPersistent = true;
            TimeMode = InstanceTimeMode.Unscaled;
        }

        public override void OnStep(double deltaTime)
        {
            _steps++;
            if (_steps == 2) PauseGameplay(SmokePause);
            if (_steps == 4) ResumeGameplay(SmokePause);
            if (_steps == 5) SwitchScene(_next, _args);
            if (_steps >= 9) _close();
        }
    }
}
