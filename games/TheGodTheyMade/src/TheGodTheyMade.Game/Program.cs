namespace TheGodTheyMade.Game;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "The God They Made - Mingzhong Valley Graybox",
            Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        using var game = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAxis2D(GameInputs.CameraMove, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
                .BindAxis2D(GameInputs.CameraMove,
                    InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down))
            .UseDefault2DRenderer(renderer => renderer.UseContent(GameAssets.Packages.Root))
            .ConfigureScene("MingzhongValley", context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.035f, 0.045f, 0.055f, 1f));
                context.Camera.Position = new Vector2(128f, 144f);
                context.Camera.Zoom = 1f;

                NavigationGrid navigation = MingzhongNavigation.CreateGrid();
                var navigationQuery = new NavigationQuery(navigation.CellCount);
                var villageDirector = new VillageDirector();
                var worldSimulation = new MingzhongWorldSimulation(
                    MingzhongVillage.Roster,
                    navigation.IsBlocked);
                var world = new MingzhongWorldInstance(
                    context.TileMaps.Get(GameAssets.TileMaps.MingzhongWorld),
                    context.TileMapRenderer,
                    context.Camera,
                    screen => context.TryScreenToWorld(screen, out Vector2D position, out _)
                        ? position
                        : null,
                    navigation,
                    worldSimulation,
                    context.Close,
                    smoke);
                context.Scene.Add(world);

                IReadOnlyList<VillagerDefinition> roster = MingzhongVillage.Roster;
                for (int i = 0; i < roster.Count; i++)
                {
                    context.Scene.Add(new VillagerInstance(
                        roster[i],
                        i,
                        navigation,
                        navigationQuery,
                        villageDirector,
                        () => world.GateBlocked,
                        worldSimulation));
                }
                context.Scene.Add(new FamiliarInstance());
            })
            .Build();

        game.Run();
    }
}
