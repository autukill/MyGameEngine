namespace ViewportNavigation.VisualTests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ViewportNavigation;

internal static class Program
{
    private static readonly Bounds2D World = new(0f, 0f, 12_000f, 12_000f);
    private static EngineWindow _window = null!;
    private static SpriteShader _shader = null!;
    private static SpriteBatch _batch = null!;
    private static WhiteTexture _white = null!;
    private static Camera2D _camera = null!;
    private static ViewportController _viewport = null!;
    private static SceneRenderPass _scenePass = null!;
    private static SceneAggregate _scene = null!;
    private static bool _smoke;
    private static int _smokeSteps;
    private static bool _pointerDown;

    private static void Main(string[] args)
    {
        _smoke = args.Contains("--smoke", StringComparer.Ordinal);
        Console.WriteLine("=== Viewport Navigation Visual Test ===");
        Console.WriteLine("左键拖拽 | 双指 Pinch | 滚轮缩放 | 边缘移动 | 惯性与边界 | ESC 退出");
        EngineWindowOptions options = EngineWindowOptions.Default with
        {
            Title = "MyGameEngine - Interactive Viewport",
            IsVisible = !_smoke,
        };
        _window = new EngineWindow(options);
        _window.OnLoad += Load;
        _window.OnStep += Step;
        _window.OnDraw += Draw;
        _window.OnResize += Resize;
        _window.OnClosing += Dispose;
        _window.Run();
    }

    private static void Load()
    {
        var gl = _window.Graphics.Gl;
        _shader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _shader };
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(_window.Width, _window.Height));
        _viewport = new ViewportNavigationBuilder()
            .Drag()
            .Pinch()
            .Wheel(new ViewportWheelOptions(smoothFrames: 6))
            .MouseEdges()
            .Decelerate()
            .ClampZoom(new ViewportClampZoomOptions(
                maxWidth: World.Width,
                maxHeight: World.Height,
                maxScale: 4f))
            .Clamp(new ViewportClampOptions(World, underflow: ViewportUnderflow.Center))
            .Build()
            .CreateController(_camera);
        _viewport.MoveCenter(new Vector2(6_000f, 6_000f));

        _scene = new SceneAggregate("ViewportNavigation.VisualTest");
        _scene.SetInput(_window.Input);
        _scene.Add(new MapGrid(_white.Handle));
        _scenePass = new SceneRenderPass("ViewportNavigation.Scene", gl, _scene, _camera);
    }

    private static void Step(double deltaTime)
    {
        Vector2D mouse = _window.Input.MousePosition;
        bool inside = mouse.X >= 0 && mouse.Y >= 0 &&
                      mouse.X < _window.Width && mouse.Y < _window.Height;
        bool pointerDown = _window.Input.IsMouseButtonDown(MouseButton.Left);
        Span<ViewportPointer> pointers = stackalloc ViewportPointer[1];
        pointers[0] = new ViewportPointer(
            PointerId.Mouse,
            PointerKind.Mouse,
            new Vector2((float)mouse.X, (float)mouse.Y),
            inside,
            isCaptured: pointerDown,
            pointerDown,
            isPrimary: true,
            wasPressed: pointerDown && !_pointerDown);
        var input = new ViewportInputFrame(
            pointers,
            new Vector2((float)mouse.X, (float)mouse.Y),
            inside,
            _window.Input.MouseScrollDelta);
        _viewport.Update(in input, deltaTime);
        _pointerDown = pointerDown;
        _camera.Update(deltaTime);
        _scene.PerformInput(_window.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(deltaTime);
        if (_window.Input.WasKeyPressed(InputKey.Escape) || _smoke && ++_smokeSteps >= 4)
            _window.NativeWindow.Close();
    }

    private static void Draw()
    {
        var context = new RenderPassContext(
            _window.Graphics.Gl,
            _shader,
            _batch,
            _window.Width,
            _window.Height);
        _scenePass.Execute(context);
    }

    private static void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || _camera is null) return;
        _camera.ResizeViewport(width, height);
        _viewport.Resize();
    }

    private static void Dispose()
    {
        _scene?.End();
        _white?.Dispose();
        _batch?.Dispose();
        _shader?.Dispose();
    }

    private sealed class MapGrid : GameInstance
    {
        private readonly uint _texture;

        public MapGrid(uint texture)
            : base(nameof(MapGrid), Vector2D.Zero, LayerDepth.Instances)
        {
            _texture = texture;
            ViewCulling = InstanceViewCullingMode.AlwaysVisible;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            batch.Draw(_texture, Vector2.Zero, new Vector2(12_000f),
                new Vector4(0.055f, 0.075f, 0.09f, 1f), new Vector4(0f, 0f, 1f, 1f));
            for (int i = 0; i <= 20; i++)
            {
                float coordinate = i * 600f;
                Vector4 color = i % 5 == 0
                    ? new Vector4(0.24f, 0.48f, 0.55f, 1f)
                    : new Vector4(0.11f, 0.23f, 0.27f, 1f);
                float thickness = i % 5 == 0 ? 6f : 2f;
                batch.Draw(_texture, new Vector2(coordinate, 0f),
                    new Vector2(thickness, 12_000f), color, new Vector4(0f, 0f, 1f, 1f));
                batch.Draw(_texture, new Vector2(0f, coordinate),
                    new Vector2(12_000f, thickness), color, new Vector4(0f, 0f, 1f, 1f));
            }

            DrawMarker(batch, new Vector2(1_200f, 1_200f), new Vector4(1f, 0.35f, 0.25f, 1f));
            DrawMarker(batch, new Vector2(6_000f, 6_000f), new Vector4(1f, 0.82f, 0.25f, 1f));
            DrawMarker(batch, new Vector2(10_800f, 10_800f), new Vector4(0.35f, 0.75f, 1f, 1f));
        }

        private void DrawMarker(ISpriteBatch batch, Vector2 center, Vector4 color) =>
            batch.Draw(
                _texture,
                center - new Vector2(100f),
                new Vector2(200f),
                color,
                new Vector4(0f, 0f, 1f, 1f));
    }
}
