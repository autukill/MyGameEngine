namespace TilemapWorldPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.Tilemaps.Domain;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Hosting;
using TilemapWorldPlayground.Content;

internal static class Program
{
    private static void Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.Ordinal);
        EngineWindowOptions options = (EngineWindowOptions.Default with
        {
            Title = "MyGameEngine Playground - Tilemap World",
            Size = new Silk.NET.Maths.Vector2D<int>(960, 540),
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate(60d);

        using var game = GameApplication
            .Create(options)
            .ConfigureInput(input => input
                .BindAxis2D(GameInputs.Move, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
                .BindAxis2D(GameInputs.Move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down))
            .UseDefault2DRenderer(renderer => renderer.UseContent(GameAssets.Packages.Root))
            .ConfigureScene("TilemapWorld", context =>
            {
                context.Scene.Background = BackgroundConfig.FromColor(
                    new Vector4(0.025f, 0.04f, 0.075f, 1f));
                TileMap map = context.TileMaps.Get(GameAssets.TileMaps.PlaygroundWorld);
                var collisions = new TileCollisionBakeBuffer();
                int collisionCount = new TileCollisionBaker(context.TileSets)
                    .BakeLayer(map, "walls", collisions, new Vector2(96, 72));
                Console.WriteLine(
                    $"[TilemapWorld] chunks={map.GetLayer("walls").AllocatedChunkCount}, " +
                    $"collisionRects={collisionCount}");
                context.Scene.Add(new TilemapWorldInstance(
                    map,
                    context.TileMapRenderer,
                    context.Camera,
                    context.Close,
                    smoke));
            })
            .Build();

        game.Run();
    }

    private static class GameInputs
    {
        public static readonly InputAxis2DRef Move = new("camera.move");
    }

    private sealed class TilemapWorldInstance : GameInstance
    {
        private readonly TileMap _map;
        private readonly TileMapRenderer _renderer;
        private readonly Camera2D _camera;
        private readonly Action _close;
        private readonly bool _smoke;
        private int _steps;

        public TilemapWorldInstance(
            TileMap map,
            TileMapRenderer renderer,
            Camera2D camera,
            Action close,
            bool smoke)
            : base("TilemapWorld", Vector2D.Zero, LayerDepth.Instances)
        {
            _map = map;
            _renderer = renderer;
            _camera = camera;
            _close = close;
            _smoke = smoke;
            ViewCulling = InstanceViewCullingMode.AlwaysVisible;
        }

        public override void OnStep(double deltaTime)
        {
            Vector2D move = InputAxis2D(GameInputs.Move);
            _camera.Position += new Vector2(move.X, move.Y) * (240f * (float)deltaTime);
            if (KeyPressed(InputKey.Escape)) _close();
            if (_smoke && ++_steps >= 4) _close();
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            if (!_camera.TryGetVisibleWorldBounds(out Bounds2D visible)) return;
            _renderer.Draw(
                batch,
                _map,
                visible,
                new Vector2(96, 72),
                new Vector4(0.2f, 0.8f, 0.42f, 1f));
        }
    }
}
