namespace Sprites.VisualTests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _shader;
    private static SpriteBatch? _batch;
    private static TextureLibrary? _textures;
    private static SpriteRef _markerSprite;
    private static SceneAggregate? _scene;
    private static readonly Vector2[] Origins =
    {
        new(220, 220), new(500, 220), new(780, 220), new(300, 500), new(700, 500)
    };

    private static void Main()
    {
        Console.WriteLine("=== Sprites Visual Test ===");
        Console.WriteLine("Loads Assets/orbiting-drone-2frame.webp through TextureLibrary.Load(path).");
        Console.WriteLine("Includes a rotating GameInstance with offset origin (16, 112).");
        Console.WriteLine("双帧动画 / 中心原点 / 旋转 / 非均匀缩放 / 水平翻转");
        Console.WriteLine("白点表示各 Sprite 的世界原点；ESC 退出。");

        _window = new EngineWindow(new EngineWindowOptions(
            Title: "Sprites Visual Test",
            Size: new Silk.NET.Maths.Vector2D<int>(1000, 700)));
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.OnClosing += HandleClosing;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        _shader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _shader };
        _textures = new TextureLibrary(gl);
        var whiteTexture = _textures.RegisterRgba(
            "visual.white", 1, 1, new byte[] { 255, 255, 255, 255 });
        string atlasPath = Path.Combine(
            AppContext.BaseDirectory, "Assets", "orbiting-drone-2frame.webp");
        var atlasTexture = _textures.Load(
            "visual.webp-atlas", atlasPath, TextureSampler.PixelArt);
        Console.WriteLine($"Loaded WebP atlas: {atlasPath}");

        var sprites = new SpriteLibrary(_textures);
        _markerSprite = sprites.RegisterSingle("visual.marker", whiteTexture, Vector2.Zero);
        var demo = sprites.RegisterGrid(
            "visual.two-frame",
            atlasTexture,
            frameSize: new Vector2(128, 128),
            origin: new Vector2(64, 64),
            frameCount: 2,
            framesPerSecond: 4f);
        var offsetOriginDemo = sprites.RegisterGrid(
            "visual.offset-origin",
            atlasTexture,
            frameSize: new Vector2(128, 128),
            origin: new Vector2(16, 112),
            frameCount: 2,
            framesPerSecond: 4f);
        _batch.SpriteResolver = sprites;

        _scene = new SceneAggregate("SpritesVisual");
        _scene.SetInput(_window.Input);
        _scene.SetSprites(sprites);
        _scene.Add(new DemoSprite(demo, Origins[0], new Vector2(.75f), 0f,
            rotationSpeed: 0f, color: Vector4.One));
        _scene.Add(new DemoSprite(demo, Origins[1], new Vector2(.75f, .375f), 0f,
            rotationSpeed: 1f, color: new Vector4(1f, .8f, .8f, 1f)));
        _scene.Add(new DemoSprite(demo, Origins[2], new Vector2(-.75f, .75f), 0f,
            rotationSpeed: 0f, color: new Vector4(.8f, 1f, .8f, 1f)));
        _scene.Add(new DemoSprite(demo, Origins[3], new Vector2(1f, .5f), MathF.PI / 4,
            rotationSpeed: -.5f, color: new Vector4(.8f, .8f, 1f, .65f)));
        _scene.Add(new OffsetOriginSprite(offsetOriginDemo, Origins[4]));
        _scene.Add(new EscapeController(() => _window.NativeWindow.Close()));
    }

    private static void HandleStep(double dt)
    {
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(dt);
    }

    private static void HandleDraw()
    {
        _shader!.Use();
        _shader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, _window!.Width, _window.Height, 0, -1, 1));

        _batch!.Begin();
        _scene!.DrawActive(_batch);
        foreach (var origin in Origins)
            _batch.DrawSpriteStretched(
                _markerSprite, 0, origin - new Vector2(3), new Vector2(6));
        _batch.End();
    }

    private static void HandleClosing()
    {
        _scene?.End();
        _textures?.Dispose();
        _batch?.Dispose();
        _shader?.Dispose();
    }

    private sealed class DemoSprite : GameInstance
    {
        private readonly float _rotationSpeed;

        public DemoSprite(SpriteRef sprite, Vector2 position, Vector2 scale,
            float rotation, float rotationSpeed, Vector4 color)
            : base(nameof(DemoSprite), new Vector2D(position.X, position.Y), LayerDepth.Instances)
        {
            Sprite = sprite;
            Color = color;
            _rotationSpeed = rotationSpeed;
            Transform = Transform with
            {
                Scale = new Vector2D(scale.X, scale.Y),
                Rotation = rotation
            };
        }

        public override void OnStep(double deltaTime)
        {
            if (_rotationSpeed == 0f) return;
            Transform = Transform with
            {
                Rotation = Transform.Rotation + _rotationSpeed * (float)deltaTime
            };
        }
    }

    private sealed class OffsetOriginSprite : GameInstance
    {
        public OffsetOriginSprite(SpriteRef sprite, Vector2 position)
            : base(nameof(OffsetOriginSprite), new Vector2D(position.X, position.Y), LayerDepth.Instances)
        {
            Sprite = sprite;
            Color = new Vector4(1f, .9f, .35f, 1f);
            Transform = Transform with
            {
                Scale = new Vector2D(.75f, .75f),
                Rotation = -MathF.PI / 6
            };
        }

        public override void OnStep(double deltaTime)
        {
            Transform = Transform with
            {
                Rotation = Transform.Rotation + .75f * (float)deltaTime
            };
        }
    }

    private sealed class EscapeController : GameInstance
    {
        private readonly Action _close;
        public EscapeController(Action close) => _close = close;
        public override void OnKeyDown(InputKey key)
        {
            if (key == InputKey.Escape) _close();
        }
    }

}
