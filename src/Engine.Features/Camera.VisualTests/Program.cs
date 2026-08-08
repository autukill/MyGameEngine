namespace Camera.VisualTests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>
/// Camera 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：
///   - 世界网格 + 彩色方块（世界固定坐标系）
///   - WASD    平移相机
///   - Q/E / 滚轮  缩放 (Zoom 0.2x~5x)
///   - R       震屏
///   - ESC     退出
///
/// 架构演示：业务逻辑（输入 / 相机控制）全部放入 GameInstance 子类，
/// Program 只做装配（窗口 + 基础设施 + 场景 + 渲染 Pass）。
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _shader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static SceneAggregate? _scene;
    private static SceneRenderPass? _scenePass;

    private static void Main()
    {
        Console.WriteLine("=== Camera Visual Test ===");
        Console.WriteLine("  WASD 平移 | Q/E/滚轮 缩放 | R 震屏 | ESC 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        _shader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl);
        _batch.DefaultShader = _shader;
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(_window.Width, _window.Height));

        // 装配场景：所有业务逻辑都是 GameInstance 子类
        _scene = new SceneAggregate("CameraDemo");
        _scene.SetInput(_window.Input);
        _scene.Add(new GridRenderer(_white.Handle));
        _scene.Add(new WorldSquares(_white.Handle));
        _scene.Add(new CameraRig(_camera, _window));

        _scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera);
    }

    private static void HandleStep(double dt)
    {
        // 输入沿事件（KeyDown/KeyUp）→ 场景实例；Step 三段式（Begin/Step/End）
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(dt);
    }

    private static void HandleDraw()
    {
        var ctx = new RenderPassContext(
            _window!.Graphics.Gl, _shader!, _batch!,
            _window.Width, _window.Height);
        _scenePass!.Execute(ctx);   // 内部自动应用相机矩阵 + 实例 RenderStyle
    }

    /// <summary>背景网格渲染器（Background 层，世界固定坐标系）</summary>
    private sealed class GridRenderer : GameInstance
    {
        private readonly uint _tex;

        public GridRenderer(uint tex)
            : base(nameof(GridRenderer), Vector2D.Zero, LayerDepth.Background)
        {
            _tex = tex;
            LayerName = SceneAggregate.LayerNameBackground;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            const float size = 4000f;
            for (float x = -size; x <= size; x += 40f)
            {
                var color = MathF.Abs(x) < 1f ? new Vector4(0.3f, 0.3f, 0.3f, 1f)
                    : new Vector4(0.18f, 0.18f, 0.18f, 1f);
                batch.Draw(_tex, new Vector2(x, -size), new Vector2(1, size * 2), color,
                    new Vector4(0, 0, 1, 1));
            }
            for (float y = -size; y <= size; y += 40f)
            {
                var color = MathF.Abs(y) < 1f ? new Vector4(0.3f, 0.3f, 0.3f, 1f)
                    : new Vector4(0.18f, 0.18f, 0.18f, 1f);
                batch.Draw(_tex, new Vector2(-size, y), new Vector2(size * 2, 1), color,
                    new Vector4(0, 0, 1, 1));
            }
        }
    }

    /// <summary>世界固定方块（Instances 层）</summary>
    private sealed class WorldSquares : GameInstance
    {
        private readonly uint _tex;

        public WorldSquares(uint tex)
            : base(nameof(WorldSquares), Vector2D.Zero, LayerDepth.Instances)
        {
            _tex = tex;
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var colors = new[]
            {
                new Vector4(1f, 0.3f, 0.3f, 1f),
                new Vector4(0.3f, 1f, 0.3f, 1f),
                new Vector4(0.3f, 0.5f, 1f, 1f),
                new Vector4(1f, 1f, 0.3f, 1f),
            };
            var offsets = new[]
            {
                new Vector2(-160, -160),
                new Vector2(40, -160),
                new Vector2(-160, 40),
                new Vector2(40, 40),
            };
            for (int i = 0; i < 4; i++)
                batch.Draw(_tex, offsets[i], new Vector2(120, 120), colors[i],
                    new Vector4(0, 0, 1, 1));

            // 原点标记
            batch.Draw(_tex, new Vector2(-6, -6), new Vector2(12, 12), Vector4.One,
                new Vector4(0, 0, 1, 1));
        }
    }

    /// <summary>
    /// 相机控制器（GMS 相机对象等价物）：
    ///   OnStep 轮询输入平移/缩放；OnKeyDown 处理 R 震屏 / ESC 退出。
    /// </summary>
    private sealed class CameraRig : GameInstance
    {
        private readonly Camera2D _camera;
        private readonly EngineWindow _window;

        public CameraRig(Camera2D camera, EngineWindow window)
            : base(nameof(CameraRig), Vector2D.Zero, LayerDepth.Instances)
        {
            _camera = camera;
            _window = window;
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnStep(double dt)
        {
            var speed = (float)(300.0 * dt);
            if (Input is not null)
            {
                if (Input.IsKeyDown(InputKey.W)) _camera.Position += new Vector2(0, -speed);
                if (Input.IsKeyDown(InputKey.S)) _camera.Position += new Vector2(0, speed);
                if (Input.IsKeyDown(InputKey.A)) _camera.Position += new Vector2(-speed, 0);
                if (Input.IsKeyDown(InputKey.D)) _camera.Position += new Vector2(speed, 0);

                var scroll = Input.MouseScrollDelta;
                if (scroll != 0f)
                    _camera.Zoom = Math.Clamp(_camera.Zoom * (scroll > 0 ? 1.1f : 0.9f), 0.2f, 5f);
            }

            _camera.Update(dt); // 震屏计时
        }

        public override void OnKeyDown(InputKey key)
        {
            switch (key)
            {
                case InputKey.Escape:
                    _window.NativeWindow.Close();
                    break;
                case InputKey.R:
                    _camera.Shake(30f, 0.3f);
                    break;
                case InputKey.Q:
                    _camera.Zoom = Math.Clamp(_camera.Zoom * 0.9f, 0.2f, 5f);
                    break;
                case InputKey.E:
                    _camera.Zoom = Math.Clamp(_camera.Zoom * 1.1f, 0.2f, 5f);
                    break;
            }
        }
    }
}
