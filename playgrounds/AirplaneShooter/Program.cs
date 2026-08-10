namespace AirplaneShooter;

using System.Numerics;
using AirplaneShooter.Content;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Hosting;
using GameEngine.Features.Audio;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "MyGameEngine Playground - Airplane Shooter",
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

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
            .UseAudio(new AudioHostingOptions(ForceSilentBackend: smoke))
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(GameAssets.Packages.Root))
            .ConfigureInstances(instances => instances.Register(
                PlayerPlane.BulletPrefab,
                spawn => new PlayerBullet(
                    GameAssets.Sprites.PlayerBullet,
                    spawn.Position)))
            .AddScene(GameScenes.Main, context =>
            {
                AudioClipRef laser = EnsureLaserClip(context.AudioClips);
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.015f, 0.025f, 0.075f, 1f));
                context.Scene.Add(new PlayerPlane(
                    GameAssets.Sprites.PlayerPlane,
                    context.Transforms,
                    context.Audio,
                    laser,
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

    private static AudioClipRef EnsureLaserClip(AudioLibrary library)
    {
        var clip = new AudioClipRef("airplane.laser");
        if (library.TryGet(clip, out _)) return clip;

        const int sampleRate = 48_000;
        const double durationSeconds = 0.07;
        int frames = (int)(sampleRate * durationSeconds);
        var pcm = new byte[frames * sizeof(short)];
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / sampleRate;
            double frequency = 1_250d - 750d * (i / (double)frames);
            double envelope = 1d - i / (double)frames;
            short sample = (short)(Math.Sin(2d * Math.PI * frequency * t) * envelope * 12_000d);
            pcm[i * 2] = (byte)sample;
            pcm[i * 2 + 1] = (byte)(sample >> 8);
        }
        return library.RegisterDecoded(
            clip.Name,
            "procedural://airplane-laser",
            new DecodedAudioClip(pcm, AudioSampleFormat.Signed16, 1, sampleRate));
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
