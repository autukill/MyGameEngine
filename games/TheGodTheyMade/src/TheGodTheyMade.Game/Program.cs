namespace TheGodTheyMade.Game;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;
using GameEngine.Features.Replay.Application;
using GameEngine.Features.Replay.Domain;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Beliefs;
using TheGodTheyMade.Simulation.Familiar;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        bool scriptedBelief = args.Contains("--scripted-belief", StringComparer.Ordinal);
        string? recordReplayPath = GetOptionValue(args, "--record-replay");
        string? playReplayPath = GetOptionValue(args, "--replay");
        if (recordReplayPath is not null && playReplayPath is not null)
            throw new ArgumentException("Use either --record-replay or --replay, not both.");
        var replayIdentity = new ReplayIdentity("the-god-they-made.mingzhong", "gate-3");
        ReplaySession? replay = recordReplayPath is not null
            ? ReplaySession.Record(replayIdentity)
            : playReplayPath is not null
                ? ReplaySession.Load(playReplayPath, replayIdentity)
                : null;
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "The God They Made - Mingzhong Valley Graybox",
            Size = new Silk.NET.Maths.Vector2D<int>(1280, 720),
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        GameApplicationBuilder builder = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAxis2D(GameInputs.CameraMove, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
                .BindAxis2D(GameInputs.CameraMove,
                    InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
                .BindAction(GameInputs.PraiseFamiliar, InputKey.Q)
                .BindAction(GameInputs.StopFamiliar, InputKey.E))
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
                var beliefSimulation = new BeliefSimulation(MingzhongVillage.Roster);
                var familiarLearning = new FamiliarLearning(0x4D494E475A484F4EUL);
                var world = new MingzhongWorldInstance(
                    context.TileMaps.Get(GameAssets.TileMaps.MingzhongWorld),
                    context.TileMapRenderer,
                    context.Camera,
                    screen => context.TryScreenToWorld(screen, out Vector2D position, out _)
                        ? position
                        : null,
                    navigation,
                    worldSimulation,
                    beliefSimulation,
                    context.Close,
                    smoke,
                    scriptedBelief,
                    replay is { Mode: ReplaySessionMode.Playback });
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
                        worldSimulation,
                        beliefSimulation));
                }
                context.Scene.Add(new FamiliarInstance(
                    familiarLearning,
                    worldSimulation,
                    navigation,
                    scriptedBelief));
            });

        if (replay is { Mode: ReplaySessionMode.Recording })
            builder.UseReplayRecording(replay);
        else if (replay is { Mode: ReplaySessionMode.Playback })
            builder.UseReplayPlayback(replay);

        using var game = builder.Build();

        game.Run();
        if (recordReplayPath is not null)
        {
            replay!.Save(recordReplayPath);
            Console.WriteLine($"Replay saved: {Path.GetFullPath(recordReplayPath)}");
        }
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        if (index < 0) return null;
        if (index == args.Length - 1 || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException($"{option} requires a file path.");
        return args[index + 1];
    }
}
