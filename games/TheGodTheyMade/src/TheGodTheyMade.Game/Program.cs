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
using TheGodTheyMade.Simulation.Scenario;
using GameEngine.Features.Audio;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        bool scriptedBelief = args.Contains("--scripted-belief", StringComparer.Ordinal) ||
                              args.Contains("--scripted-regression", StringComparer.Ordinal);
        string? recordReplayPath = GetOptionValue(args, "--record-replay");
        string? playReplayPath = GetOptionValue(args, "--replay");
        string? recordCommandsPath = GetOptionValue(args, "--record-commands");
        string? playCommandsPath = GetOptionValue(args, "--play-commands");
        string? playtestReportPath = GetOptionValue(args, "--playtest-report");
        string? testerId = GetOptionValue(args, "--tester-id");
        if (recordReplayPath is not null && playReplayPath is not null)
            throw new ArgumentException("Use either --record-replay or --replay, not both.");
        if ((recordReplayPath is not null || playReplayPath is not null) && !scriptedBelief)
            throw new ArgumentException(
                "Replay v1 cannot encode dynamic pointer world positions. " +
                "Use --scripted-regression with --record-replay/--replay.");
        if (recordCommandsPath is not null && playCommandsPath is not null)
            throw new ArgumentException("Use either --record-commands or --play-commands, not both.");
        if ((recordReplayPath is not null || playReplayPath is not null) &&
            (recordCommandsPath is not null || playCommandsPath is not null))
            throw new ArgumentException("Replay Bundle and Gameplay Command Journal cannot be combined.");
        MingzhongCommandJournal commandJournal = recordCommandsPath is not null
            ? MingzhongCommandJournal.Record()
            : playCommandsPath is not null
                ? MingzhongCommandJournal.Play(MingzhongCommandRecordingFormat.Read(playCommandsPath))
                : MingzhongCommandJournal.Disabled();
        var replayIdentity = new ReplayIdentity("the-god-they-made.mingzhong", "gate-4");
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
        MingzhongWorldSimulation? sessionWorld = null;
        BeliefSimulation? sessionBeliefs = null;
        FamiliarLearning? sessionFamiliar = null;
        MingzhongIslandScenario? sessionScenario = null;

        GameApplicationBuilder builder = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAxis2D(GameInputs.CameraMove, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
                .BindAxis2D(GameInputs.CameraMove,
                    InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
                .BindAction(GameInputs.PraiseFamiliar, InputKey.Q)
                .BindAction(GameInputs.StopFamiliar, InputKey.E))
            .UseAudio()
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
                var islandScenario = new MingzhongIslandScenario();
                sessionWorld = worldSimulation;
                sessionBeliefs = beliefSimulation;
                sessionFamiliar = familiarLearning;
                sessionScenario = islandScenario;
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
                    familiarLearning,
                    islandScenario,
                    commandJournal,
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
                context.Scene.Add(new ScenarioAudioFeedback(
                    worldSimulation,
                    context.Audio,
                    RegisterTone(context.AudioClips, "mingzhong.bell", 330f, 0.22f),
                    RegisterTone(context.AudioClips, "mingzhong.rain", 620f, 0.16f),
                    RegisterTone(context.AudioClips, "mingzhong.gate", 120f, 0.28f),
                    RegisterTone(context.AudioClips, "mingzhong.funeral", 220f, 0.34f)));
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
        if (recordCommandsPath is not null)
        {
            MingzhongCommandRecordingFormat.Write(
                recordCommandsPath,
                commandJournal.Snapshot(sessionWorld!.Tick));
            Console.WriteLine($"Gameplay commands saved: {Path.GetFullPath(recordCommandsPath)}");
        }
        if (playtestReportPath is not null)
        {
            string reportTesterId = string.IsNullOrWhiteSpace(testerId)
                ? Path.GetFileNameWithoutExtension(playtestReportPath)
                : testerId;
            PlaytestReportWriter.Write(
                playtestReportPath,
                reportTesterId,
                sessionWorld!,
                sessionBeliefs!,
                sessionFamiliar!,
                sessionScenario!,
                commandJournal);
            Console.WriteLine($"Playtest report saved: {Path.GetFullPath(playtestReportPath)}");
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

    private static AudioClipRef RegisterTone(
        AudioLibrary library,
        string name,
        float frequency,
        float durationSeconds)
    {
        const int sampleRate = 22_050;
        int sampleCount = (int)(sampleRate * durationSeconds);
        var pcm = new byte[sampleCount * sizeof(short)];
        for (int i = 0; i < sampleCount; i++)
        {
            float envelope = 1f - i / (float)sampleCount;
            short sample = (short)(MathF.Sin(MathF.Tau * frequency * i / sampleRate) * envelope * 8_000f);
            pcm[i * 2] = (byte)sample;
            pcm[i * 2 + 1] = (byte)(sample >> 8);
        }
        return library.RegisterDecoded(
            name,
            $"memory://{name}.pcm",
            new DecodedAudioClip(pcm, AudioSampleFormat.Signed16, 1, sampleRate));
    }
}
