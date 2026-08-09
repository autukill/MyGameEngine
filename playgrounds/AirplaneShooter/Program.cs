namespace AirplaneShooter;

using System.Numerics;
using AirplaneShooter.Content;
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
        EngineWindowOptions options = EngineWindowOptions.Default with
        {
            Title = "MyGameEngine Playground - Airplane Shooter",
            IsVisible = !smoke,
            VSync = !smoke,
            FixedDeltaTime = smoke ? 1d / 60d : null
        };

        using var game = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAxis2D(
                    GameInputs.Move,
                    InputKey.A,
                    InputKey.D,
                    InputKey.W,
                    InputKey.S)
                .BindAxis2D(
                    GameInputs.Move,
                    InputKey.Left,
                    InputKey.Right,
                    InputKey.Up,
                    InputKey.Down)
                .BindAction(GameInputs.Fire, InputKey.Space)
                .BindAction(GameInputs.Restart, InputKey.Enter))
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(GameAssets.Packages.Root))
            .ConfigureInstances(instances => instances.Register(
                PlayerPlane.BulletPrefab,
                spawn => new PlayerBullet(
                    GameAssets.Sprites.PlayerBullet,
                    spawn.Position)))
            .AddScene(GameScenes.Main, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.015f, 0.025f, 0.075f, 1f));
                context.Scene.Add(new PlayerPlane(
                    GameAssets.Sprites.PlayerPlane,
                    new Vector2D(
                        context.Window.Width * 0.5f,
                        context.Window.Height - 90f),
                    context.Window.Width,
                    context.Window.Height));
                context.Scene.Add(new Target(
                    GameAssets.Sprites.PlayerBullet,
                    new Vector2D(context.Window.Width * 0.5f, 100f)));

                if (smoke)
                    context.Scene.Add(new SmokeJourney(GameScenes.Victory, context.Close));
            })
            .AddScene(GameScenes.Victory, context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.015f, 0.12f, 0.08f, 1f));
                context.Scene.Add(new VictoryMarker(
                    GameAssets.Sprites.PlayerPlane,
                    new Vector2D(
                        context.Window.Width * 0.5f,
                        context.Window.Height * 0.5f)));
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
            if (_steps == 2) SwitchScene(_next);
            if (_steps >= 5) _close();
        }
    }
}
