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
using GameEngine.Hosting;

/// <summary>
/// Camera 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：
///   - 世界网格 + Reference View / Design Safe Frame / Overscan
///   - TAB     切换 FixedHeight / FixedWidth / Expand / Cover / MatchRenderTarget
///   - WASD    平移相机
///   - Q/E / 滚轮  缩放 (Zoom 0.2x~5x)
///   - SPACE   重置当前构图策略
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
    private static bool _smoke;
    private static readonly SceneCameraState ReferenceCamera = new(
        new Vector2(-ReferenceWidth * .5f, -ReferenceHeight * .5f));
    private static readonly FramingPreset[] FramingPresets =
    [
        new("FixedHeight", SceneCameraViewportPolicy.FixedVisibleHeight(
            ReferenceWidth, ReferenceHeight)),
        new("FixedWidth", SceneCameraViewportPolicy.FixedVisibleWidth(
            ReferenceWidth, ReferenceHeight)),
        new("Expand / Show All", SceneCameraViewportPolicy.Expand(
            ReferenceWidth, ReferenceHeight)),
        new("Cover / Fill", SceneCameraViewportPolicy.Cover(
            ReferenceWidth, ReferenceHeight)),
        new("MatchRenderTarget", SceneCameraViewportPolicy.MatchRenderTarget)
    ];
    private static int _framingIndex;

    private const float ReferenceWidth = 960f;
    private const float ReferenceHeight = 540f;
    private const float SafeWidth = 800f;
    private const float SafeHeight = 450f;
    private const float OverscanWidth = 1280f;
    private const float OverscanHeight = 720f;

    private static void Main(string[] args)
    {
        _smoke = args.Contains("--smoke", StringComparer.Ordinal);
        Console.WriteLine("=== Camera Visual Test ===");
        Console.WriteLine(
            "  TAB 构图策略 | SPACE 重置 | WASD 平移 | Q/E/滚轮 缩放 | R 震屏 | ESC 退出");

        _window = new EngineWindow(EngineWindowOptions.Default with
        {
            Title = "Camera Framing Visual Test",
            Size = new Silk.NET.Maths.Vector2D<int>(960, 540),
            IsVisible = !_smoke,
            VSync = !_smoke
        });
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.OnResize += HandleResize;
        _window.OnClosing += HandleClosing;
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
        CurrentFraming.Policy.Activate(_camera, ReferenceCamera);

        // 装配场景：所有业务逻辑都是 GameInstance 子类
        _scene = new SceneAggregate("CameraDemo");
        _scene.SetInput(_window.Input);
        _scene.Add(new GridRenderer(_white.Handle));
        _scene.Add(new FramingGuide(_white.Handle));
        _scene.Add(new CameraRig(_camera, _window));
        if (_smoke) _scene.Add(new SmokeProbe(_camera, _window));

        _scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera);
        UpdateWindowTitle();
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

    private static FramingPreset CurrentFraming => FramingPresets[_framingIndex];

    private static void HandleResize(int width, int height)
    {
        if (_camera is null || width <= 0 || height <= 0) return;
        CurrentFraming.Policy.Resize(_camera, width, height);
        UpdateWindowTitle();
    }

    private static void CycleFraming()
    {
        _framingIndex = (_framingIndex + 1) % FramingPresets.Length;
        CurrentFraming.Policy.Activate(_camera!, ReferenceCamera);
        UpdateWindowTitle();
        Console.WriteLine($"  Framing: {CurrentFraming.Name}");
    }

    private static void ResetFraming()
    {
        CurrentFraming.Policy.Activate(_camera!, ReferenceCamera);
        UpdateWindowTitle();
    }

    private static void UpdateWindowTitle()
    {
        if (_window is null || _camera is null ||
            !_camera.TryGetStableVisibleWorldBounds(out var visible)) return;
        _window.NativeWindow.Title =
            $"Camera Framing — {CurrentFraming.Name} — " +
            $"View {visible.Width:F0}×{visible.Height:F0} — " +
            $"Window {_window.Width}×{_window.Height}";
    }

    private static void HandleClosing()
    {
        _white?.Dispose();
        _batch?.Dispose();
        _shader?.Dispose();
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

    /// <summary>显示 Overscan、Reference View 和 Design Safe Frame 的世界空间标尺。</summary>
    private sealed class FramingGuide : GameInstance
    {
        private readonly uint _tex;

        public FramingGuide(uint tex)
            : base(nameof(FramingGuide), Vector2D.Zero, LayerDepth.Instances)
        {
            _tex = tex;
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            DrawCenteredRect(batch, OverscanWidth, OverscanHeight,
                new Vector4(.08f, .16f, .28f, 1f));
            DrawCenteredRect(batch, ReferenceWidth, ReferenceHeight,
                new Vector4(.16f, .30f, .48f, 1f));
            DrawCenteredRect(batch, SafeWidth, SafeHeight,
                new Vector4(.18f, .48f, .32f, 1f));

            DrawBorder(batch, OverscanWidth, OverscanHeight, 5f,
                new Vector4(.25f, .55f, 1f, 1f));
            DrawBorder(batch, ReferenceWidth, ReferenceHeight, 5f,
                new Vector4(1f, .78f, .2f, 1f));
            DrawBorder(batch, SafeWidth, SafeHeight, 5f,
                new Vector4(.3f, 1f, .55f, 1f));

            DrawCornerMarkers(batch, ReferenceWidth, ReferenceHeight,
                new Vector4(1f, .35f, .2f, 1f));
            batch.Draw(_tex, new Vector2(-6f, -6f), new Vector2(12f, 12f),
                Vector4.One, new Vector4(0, 0, 1, 1));
        }

        private void DrawCenteredRect(
            ISpriteBatch batch,
            float width,
            float height,
            Vector4 color) =>
            batch.Draw(_tex, new Vector2(-width * .5f, -height * .5f),
                new Vector2(width, height), color, new Vector4(0, 0, 1, 1));

        private void DrawBorder(
            ISpriteBatch batch,
            float width,
            float height,
            float thickness,
            Vector4 color)
        {
            float left = -width * .5f;
            float top = -height * .5f;
            batch.Draw(_tex, new Vector2(left, top), new Vector2(width, thickness),
                color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(left, top + height - thickness),
                new Vector2(width, thickness), color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(left, top), new Vector2(thickness, height),
                color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(left + width - thickness, top),
                new Vector2(thickness, height), color, new Vector4(0, 0, 1, 1));
        }

        private void DrawCornerMarkers(
            ISpriteBatch batch,
            float width,
            float height,
            Vector4 color)
        {
            const float marker = 28f;
            float left = -width * .5f;
            float top = -height * .5f;
            batch.Draw(_tex, new Vector2(left, top), new Vector2(marker, marker),
                color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(-left - marker, top), new Vector2(marker, marker),
                color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(left, -top - marker), new Vector2(marker, marker),
                color, new Vector4(0, 0, 1, 1));
            batch.Draw(_tex, new Vector2(-left - marker, -top - marker),
                new Vector2(marker, marker), color, new Vector4(0, 0, 1, 1));
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
            bool changed = false;
            if (Input is not null)
            {
                if (Input.IsKeyDown(InputKey.W))
                {
                    _camera.Position += new Vector2(0, -speed);
                    changed = true;
                }
                if (Input.IsKeyDown(InputKey.S))
                {
                    _camera.Position += new Vector2(0, speed);
                    changed = true;
                }
                if (Input.IsKeyDown(InputKey.A))
                {
                    _camera.Position += new Vector2(-speed, 0);
                    changed = true;
                }
                if (Input.IsKeyDown(InputKey.D))
                {
                    _camera.Position += new Vector2(speed, 0);
                    changed = true;
                }

                var scroll = Input.MouseScrollDelta;
                if (scroll != 0f)
                {
                    _camera.Zoom = Math.Clamp(_camera.Zoom * (scroll > 0 ? 1.1f : 0.9f), 0.2f, 5f);
                    changed = true;
                }
            }

            _camera.Update(dt); // 震屏计时
            if (changed) UpdateWindowTitle();
        }

        public override void OnKeyDown(InputKey key)
        {
            switch (key)
            {
                case InputKey.Escape:
                    _window.NativeWindow.Close();
                    break;
                case InputKey.Tab:
                    CycleFraming();
                    break;
                case InputKey.Space:
                    ResetFraming();
                    break;
                case InputKey.R:
                    _camera.Shake(30f, 0.3f);
                    break;
                case InputKey.Q:
                    _camera.Zoom = Math.Clamp(_camera.Zoom * 0.9f, 0.2f, 5f);
                    UpdateWindowTitle();
                    break;
                case InputKey.E:
                    _camera.Zoom = Math.Clamp(_camera.Zoom * 1.1f, 0.2f, 5f);
                    UpdateWindowTitle();
                    break;
            }
        }
    }

    private sealed record FramingPreset(
        string Name,
        SceneCameraViewportPolicy Policy);

    private sealed class SmokeProbe : GameInstance
    {
        private readonly Camera2D _camera;
        private readonly EngineWindow _window;
        private int _steps;

        public SmokeProbe(Camera2D camera, EngineWindow window)
            : base(nameof(SmokeProbe), Vector2D.Zero, LayerDepth.Instances)
        {
            _camera = camera;
            _window = window;
        }

        public override void OnStep(double deltaTime)
        {
            if (!_camera.TryGetStableVisibleWorldBounds(out var visible) ||
                visible.Width <= 0f || visible.Height <= 0f)
            {
                throw new InvalidOperationException(
                    "Camera Framing smoke requires finite positive visible bounds.");
            }
            _steps++;
            if (_steps == 2) CycleFraming();
            if (_steps >= 4) _window.NativeWindow.Close();
        }
    }
}
