namespace Sprites.VisualTests;

using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Sprites.Infrastructure;

internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _shader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static DemoAtlasTexture? _atlas;
    private static SceneAggregate? _scene;
    private static readonly Vector2[] Origins =
    {
        new(220, 220), new(500, 220), new(780, 220), new(300, 500), new(700, 500)
    };

    private static void Main()
    {
        Console.WriteLine("=== Sprites Visual Test ===");
        Console.WriteLine("Includes a rotating GameInstance with offset origin (4, 28).");
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
        _white = new WhiteTexture(gl);
        _atlas = new DemoAtlasTexture(gl);

        var sprites = new SpriteLibrary();
        var demo = sprites.RegisterGrid(
            "visual.two-frame",
            _atlas.Handle,
            textureSize: new Vector2(64, 32),
            frameSize: new Vector2(32, 32),
            origin: new Vector2(16, 16),
            frameCount: 2,
            framesPerSecond: 4f);
        var offsetOriginDemo = sprites.RegisterGrid(
            "visual.offset-origin",
            _atlas.Handle,
            textureSize: new Vector2(64, 32),
            frameSize: new Vector2(32, 32),
            origin: new Vector2(4, 28),
            frameCount: 2,
            framesPerSecond: 4f);
        _batch.SpriteResolver = sprites;

        _scene = new SceneAggregate("SpritesVisual");
        _scene.SetInput(_window.Input);
        _scene.SetSprites(sprites);
        _scene.Add(new DemoSprite(demo, Origins[0], new Vector2(3), 0f,
            rotationSpeed: 0f, color: Vector4.One));
        _scene.Add(new DemoSprite(demo, Origins[1], new Vector2(3, 1.5f), 0f,
            rotationSpeed: 1f, color: new Vector4(1f, .8f, .8f, 1f)));
        _scene.Add(new DemoSprite(demo, Origins[2], new Vector2(-3, 3), 0f,
            rotationSpeed: 0f, color: new Vector4(.8f, 1f, .8f, 1f)));
        _scene.Add(new DemoSprite(demo, Origins[3], new Vector2(4, 2), MathF.PI / 4,
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
            _batch.Draw(_white!.Handle, origin - new Vector2(3), new Vector2(6), Vector4.One,
                new Vector4(0, 0, 1, 1));
        _batch.End();
    }

    private static void HandleClosing()
    {
        _scene?.End();
        _atlas?.Dispose();
        _white?.Dispose();
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
                Scale = new Vector2D(3, 3),
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

    private sealed class DemoAtlasTexture : IDisposable
    {
        private readonly GL _gl;
        public uint Handle { get; }

        public unsafe DemoAtlasTexture(GL gl)
        {
            _gl = gl;
            Handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, Handle);

            var pixels = new byte[64 * 32 * 4];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 64; x++)
            {
                bool first = x < 32;
                bool accent = ((x % 32) / 8 + y / 8) % 2 == 0;
                int p = (y * 64 + x) * 4;
                pixels[p + 0] = first ? (byte)255 : (byte)(accent ? 40 : 20);
                pixels[p + 1] = first ? (byte)(accent ? 80 : 30) : (byte)220;
                pixels[p + 2] = first ? (byte)40 : (byte)255;
                pixels[p + 3] = 255;
            }

            fixed (byte* p = pixels)
                gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                    64, 32, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);

            uint nearest = (uint)GLEnum.Nearest;
            uint clamp = (uint)GLEnum.ClampToEdge;
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in nearest);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in nearest);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, in clamp);
            gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, in clamp);
        }

        public void Dispose() => _gl.DeleteTexture(Handle);
    }
}
