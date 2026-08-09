namespace MyGameTemplate;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;
using MyGameTemplate.Content;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        var options = smoke
            ? EngineWindowOptions.Default with
            {
                IsVisible = false,
                VSync = false,
                FixedDeltaTime = 1d / 60d
            }
            : EngineWindowOptions.Default with { Title = "MyGameTemplate" };

        using var game = GameApplication
            .Create(options)
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(GameAssets.Packages.Root))
            .ConfigureInstances(instances => instances.Register(
                Player.BulletPrefab,
                spawn => new Bullet(GameAssets.Sprites.Player, spawn.Position)))
            .ConfigureScene("Main", context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.06f, 0.08f, 0.12f, 1f));
                context.Scene.Add(new Player(
                    GameAssets.Sprites.Player,
                    new Vector2D(
                        context.Window.Width * 0.5f,
                        context.Window.Height * 0.5f)));
                if (smoke) context.Scene.Add(new SmokeExit(context.Close));
            })
            .Build();

        game.Run();
    }

    private sealed class SmokeExit(Action close) : GameInstance
    {
        private int _steps;

        public override void OnStep(double deltaTime)
        {
            if (++_steps >= 3) close();
        }
    }
}
