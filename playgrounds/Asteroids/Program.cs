namespace AsteroidsPlayground;

using System.Numerics;
using AsteroidsPlayground.Content;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        EngineWindowOptions options = EngineWindowOptions.Default with
        {
            Title = "MyGameEngine Playground - Asteroids",
            IsVisible = !smoke,
            VSync = !smoke,
            FixedDeltaTime = smoke ? 1d / 60d : null
        };

        using var game = GameApplication
            .Create(options)
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(GameAssets.Packages.Root))
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
                context.Scene.Add(new PlayerShip(
                    GameAssets.Sprites.AsteroidsShip,
                    new Vector2D(context.Window.Width * 0.5f, context.Window.Height * 0.5f),
                    context.Window.Width,
                    context.Window.Height));
                context.Scene.Add(new AsteroidSpawner(
                    context.Window.Width,
                    context.Window.Height));
                if (smoke)
                    context.Scene.Add(new SmokeJourney(GameScenes.GameOver, context.Close));
            })
            .AddScene(GameScenes.GameOver, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.14f, 0.015f, 0.02f, 1f));
                context.Scene.Add(new GameOverMarker(
                    GameAssets.Sprites.AsteroidsShip,
                    new Vector2D(context.Window.Width * 0.5f, context.Window.Height * 0.5f)));
            })
            .StartScene(GameScenes.Main)
            .Build();

        game.Run();
    }

    private sealed class SmokeJourney : GameInstance
    {
        private readonly SceneRef _next;
        private readonly Action _close;
        private int _steps;

        public SmokeJourney(SceneRef next, Action close)
        {
            _next = next;
            _close = close;
            IsPersistent = true;
        }

        public override void OnStep(double deltaTime)
        {
            _steps++;
            if (_steps == 3) SwitchScene(_next);
            if (_steps >= 7) _close();
        }
    }
}
