namespace AirplaneShooter;

using System.Numerics;
using AirplaneShooter.Content;
using GameEngine.Core.Domain.Entities;
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
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(GameAssets.Packages.Root))
            .ConfigureScene("Main", context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.015f, 0.025f, 0.075f, 1f));
                context.Scene.Add(new PlayerPlane(
                    GameAssets.Sprites.PlayerPlane,
                    GameAssets.Sprites.PlayerBullet,
                    new Vector2D(
                        context.Window.Width * 0.5f,
                        context.Window.Height - 90f),
                    context.Window.Width,
                    context.Window.Height));

                if (smoke)
                    context.Scene.Add(new SmokeExit(context.Close));
            })
            .Build();

        game.Run();
    }

    private sealed class SmokeExit(Action close) : GameInstance
    {
        private int _steps;

        public override void OnStep(double deltaTime)
        {
            if (++_steps >= 5) close();
        }
    }
}
