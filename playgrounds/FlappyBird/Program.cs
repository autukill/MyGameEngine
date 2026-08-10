namespace FlappyBirdPlayground;

using System.Numerics;
using FlappyBirdPlayground.Content;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Audio;
using GameEngine.Hosting;

internal static class Program {
    private const float WorldWidth = 960f;
    private const float WorldHeight = 540f;
    private const float GroundTop = 500f;

    private static void Main( string[] args ) {
        bool smoke = args.Contains( "--smoke", StringComparer.Ordinal );
        EngineWindowOptions options = (EngineWindowOptions.Default with {
            Title = "MyGameEngine Playground - Flappy Bird",
            Size = new Silk.NET.Maths.Vector2D<int>( (int)WorldWidth, (int)WorldHeight ),
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate( 60d );

        using var game = GameApplication
            .Create( options )
            .ConfigureInput( input => input
                .BindAction( GameInputs.Flap, InputKey.Space, InputKey.Up, InputKey.W )
                .BindAction( GameInputs.Restart, InputKey.Enter ) )
            .UseAudio( new AudioHostingOptions( ForceSilentBackend: smoke ) )
            .UseDefault2DRenderer( renderer => renderer.UseContent( GameAssets.Packages.Root ) )
            .ConfigureInstances( instances => instances
                .Register(
                    GamePrefabs.Pipe,
                    ( in PipeSpawnArgs spawn ) =>
                        new PipeObstacle( GameAssets.Sprites.FlappyShape, spawn ) )
                .Register(
                    GamePrefabs.ScoreGate,
                    ( in ScoreGateSpawnArgs spawn ) => new ScoreGate( spawn ) ) )
            .AddScene( GameScenes.Main, context => {
                AudioSet sounds = EnsureAudio( context.AudioClips );
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4( 0.12f, 0.64f, 0.82f, 1f ) );

                var bird = new Bird(
                    GameAssets.Sprites.FlappyShape,
                    context.Audio,
                    sounds.Flap,
                    sounds.Score,
                    sounds.Hit,
                    new Vector2D( 240f, 250f ),
                    GroundTop,
                    context.Close );

                context.Scene.Add( new SkyBackdrop(
                    GameAssets.Sprites.FlappyShape,
                    WorldWidth,
                    GroundTop ) );
                context.Scene.Add( bird );
                context.Scene.Add( new PipeSpawner( bird.ToInstanceRef(), WorldWidth, GroundTop ) );
                context.Scene.Add( new GroundStrip(
                    GameAssets.Sprites.FlappyShape,
                    WorldWidth,
                    GroundTop,
                    WorldHeight ) );

                if ( smoke )
                    context.Scene.Add( new SmokeJourney( GameScenes.GameOver, context.Close ) );
            } )
            .AddScene( GameScenes.GameOver, ( context, gameOver ) => {
                GameSession.RecordScore( gameOver.Score );
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4( 0.06f, 0.25f, 0.34f, 1f ) );
                context.Scene.Add( new SkyBackdrop(
                    GameAssets.Sprites.FlappyShape,
                    WorldWidth,
                    GroundTop ) );
                context.Scene.Add( new GroundStrip(
                    GameAssets.Sprites.FlappyShape,
                    WorldWidth,
                    GroundTop,
                    WorldHeight ) );
                context.Scene.Add( new GameOverCard(
                    GameAssets.Sprites.FlappyShape,
                    gameOver.Score,
                    context.Close ) );
            } )
            .StartScene( GameScenes.Main )
            .Build();

        game.Run();
    }

    private static AudioSet EnsureAudio( AudioLibrary clips ) {
        AudioClipRef flap = EnsureTone( clips, "flappy.flap", 620d, 980d, 0.07d, 0.32f );
        AudioClipRef score = EnsureTone( clips, "flappy.score", 980d, 1_520d, 0.11d, 0.28f );
        AudioClipRef hit = EnsureTone( clips, "flappy.hit", 170d, 70d, 0.18d, 0.35f );
        return new AudioSet( flap, score, hit );
    }

    private static AudioClipRef EnsureTone(
        AudioLibrary library,
        string name,
        double startFrequency,
        double endFrequency,
        double durationSeconds,
        float volume ) {
        var clip = new AudioClipRef( name );
        if ( library.TryGet( clip, out _ ) ) return clip;

        const int sampleRate = 48_000;
        int frameCount = (int)(sampleRate * durationSeconds);
        var pcm = new byte[frameCount * sizeof(short)];
        double phase = 0d;
        for (int frame = 0; frame < frameCount; frame++) {
            double progress = frame / (double)Math.Max( 1, frameCount - 1 );
            double frequency = startFrequency + (endFrequency - startFrequency) * progress;
            phase += 2d * Math.PI * frequency / sampleRate;
            double envelope = 1d - progress;
            short sample = (short)(Math.Sin( phase ) * envelope * volume * short.MaxValue);
            pcm[frame * 2] = (byte)sample;
            pcm[frame * 2 + 1] = (byte)(sample >> 8);
        }

        return library.RegisterDecoded(
            name,
            $"procedural://{name}",
            new DecodedAudioClip( pcm, AudioSampleFormat.Signed16, 1, sampleRate ) );
    }

    private readonly record struct AudioSet(
        AudioClipRef Flap,
        AudioClipRef Score,
        AudioClipRef Hit );

    private sealed class SmokeJourney : GameInstance {
        private readonly SceneRef<GameOverArgs> _next;
        private readonly Action _close;
        private int _steps;

        public SmokeJourney( SceneRef<GameOverArgs> next, Action close ) {
            _next = next;
            _close = close;
            IsPersistent = true;
            TimeMode = InstanceTimeMode.Unscaled;
        }

        public override void OnStep( double deltaTime ) {
            _steps++;
            if ( _steps == 1 ) {
                Spawn( GamePrefabs.Pipe, new PipeSpawnArgs(
                    new Vector2D( 760f, 100f ),
                    Width: 76f,
                    Height: 200f,
                    Speed: 0f,
                    IsTop: true ) );
                Spawn( GamePrefabs.ScoreGate, new ScoreGateSpawnArgs(
                    new Vector2D( 760f, 280f ),
                    Width: 18f,
                    Height: 140f,
                    Speed: 0f ) );
            }

            if ( _steps == 2 &&
                 (CountInstances<PipeObstacle>() != 1 || CountInstances<ScoreGate>() != 1) ) {
                throw new InvalidOperationException(
                    "Flappy Bird smoke failed to assemble parameterized Prefabs." );
            }

            if ( _steps == 3 ) SwitchScene( _next, new GameOverArgs( 7 ) );
            if ( _steps >= 7 ) _close();
        }
    }
}