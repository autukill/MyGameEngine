namespace SceneSystem.VisualTests;

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
/// SceneSystem 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：SceneAggregate 多 Layer 分层渲染（GMS Room 等价物）
///   - Background Layer（最底）：装饰色块
///   - Instances Layer（中间）：圆周运动的精灵
///   - UI Layer（最顶）：半透明 UI 面板 + 标题文字（用方块代替）
///
/// 操作（全部由 LayerToggleController 实例处理）：
///   - 空格：切换 Instances 图层可见性
///   - B   ：切换 Background 图层可见性
///   - ESC ：退出
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static SceneAggregate? _scene;
    private static SceneRenderPass? _scenePass;

    private static void Main()
    {
        Console.WriteLine("=== SceneSystem Visual Test (SceneAggregate Layers) ===");
        Console.WriteLine("  空格: 切换 Instances 层 | B: 切换 Background 层 | ESC: 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        var (vw, vh) = (_window.Width, _window.Height);

        _spriteShader = new SpriteShader(gl);
        _batch = new SpriteBatch(gl);
        _batch.DefaultShader = _spriteShader;
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(vw, vh));

        // 装配场景：实例 + 输入
        _scene = new SceneAggregate("LayerDemo");
        _scene.SetInput(_window.Input);
        _scene.ViewportWidth = vw;
        _scene.ViewportHeight = vh;
        _scene.Background = BackgroundConfig.FromColor(new Vector4(0.08f, 0.09f, 0.12f, 1f));

        // Background Layer 实例
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 4; c++)
                _scene.Add(new BackgroundTile(
                    new Vector2(60 + c * 150, 60 + r * 120),
                    new Vector4(0.16f, 0.18f, 0.24f, 1f), _white.Handle));

        // Instances Layer 实例（圆周运动）
        _scene.Add(new OrbitSprite(_white.Handle,
            new Vector2D(vw * 0.5f, vh * 0.5f), 160f, 0f,
            new Vector4(1f, 0.4f, 0.4f, 1f), SceneAggregate.LayerNameInstances));
        _scene.Add(new OrbitSprite(_white.Handle,
            new Vector2D(vw * 0.5f, vh * 0.5f), 100f, MathF.PI,
            new Vector4(0.4f, 1f, 0.5f, 1f), SceneAggregate.LayerNameInstances));

        // UI Layer 实例（顶部标题 + 底部状态条）
        _scene.Add(new UIBar(new Vector2(vw * 0.5f, 40f), new Vector2(240, 24),
            new Vector4(0.2f, 0.4f, 0.8f, 0.9f), _white.Handle));
        _scene.Add(new UIBar(new Vector2(vw * 0.5f, vh - 30f), new Vector2(vw - 40, 20),
            new Vector4(0.2f, 0.2f, 0.25f, 0.7f), _white.Handle));

        // 输入控制器（空格/B 切层、ESC 退出）
        _scene.Add(new LayerToggleController(_scene, _window));

        _scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera);
    }

    private static void HandleStep(double dt)
    {
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(dt);
    }

    private static void HandleDraw()
    {
        var ctx = new RenderPassContext(
            _window!.Graphics.Gl, _spriteShader!, _batch!,
            _window.Width, _window.Height);
        _scenePass!.Execute(ctx);
    }

    /// <summary>图层可见性切换控制器：输入事件进 GameInstance（GMS 键盘事件等价物）</summary>
    private sealed class LayerToggleController : GameInstance
    {
        private readonly SceneAggregate _scene;
        private readonly EngineWindow _window;

        public LayerToggleController(SceneAggregate scene, EngineWindow window)
            : base(nameof(LayerToggleController), Vector2D.Zero, LayerDepth.Instances)
        {
            _scene = scene;
            _window = window;
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnKeyDown(InputKey key)
        {
            switch (key)
            {
                case InputKey.Escape:
                    _window.NativeWindow.Close();
                    break;
                case InputKey.Space:
                    ToggleLayer(SceneAggregate.LayerNameInstances);
                    break;
                case InputKey.B:
                    ToggleLayer(SceneAggregate.LayerNameBackground);
                    break;
            }
        }

        private void ToggleLayer(string layerName)
        {
            var cfg = _scene.FindLayerConfig(layerName);
            if (cfg is null) return;
            _scene.SetLayerVisible(layerName, !cfg.Value.IsVisible);
            Console.WriteLine($"[Layer] '{layerName}' visible={!cfg.Value.IsVisible}");
        }
    }

    /// <summary>Background 层实例：静态装饰色块</summary>
    private sealed class BackgroundTile : GameInstance
    {
        private readonly uint _tex;
        private readonly Vector4 _color;

        public BackgroundTile(Vector2 pos, Vector4 color, uint tex)
            : base(nameof(BackgroundTile), new Vector2D(pos.X, pos.Y), LayerDepth.Background)
        {
            _tex = tex;
            _color = color;
            LayerName = SceneAggregate.LayerNameBackground;
        }

        public override void OnDraw(ISpriteBatch batch) =>
            batch.Draw(_tex, new Vector2(Transform.Position.X - 60, Transform.Position.Y - 40),
                new Vector2(120, 80), _color, new Vector4(0, 0, 1, 1));
    }

    /// <summary>Instances 层实例：圆周运动精灵</summary>
    private sealed class OrbitSprite : GameInstance
    {
        private readonly uint _tex;
        private readonly Vector2D _center;
        private readonly float _radius;
        private readonly float _phase;
        private readonly Vector4 _color;
        private float _t;

        public OrbitSprite(uint tex, Vector2D center, float radius, float phase,
            Vector4 color, string layer)
            : base(nameof(OrbitSprite), center, LayerDepth.Instances)
        {
            _tex = tex;
            _center = center;
            _radius = radius;
            _phase = phase;
            _color = color;
            LayerName = layer;
        }

        public override void OnStep(double dt)
        {
            _t += (float)dt;
            var pos = new Vector2D(
                _center.X + MathF.Cos(_t + _phase) * _radius,
                _center.Y + MathF.Sin(_t + _phase) * _radius);
            Transform = Transform with { Position = pos };
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var p = Transform.Position;
            batch.Draw(_tex, new Vector2(p.X - 30, p.Y - 30), new Vector2(60, 60), _color,
                new Vector4(0, 0, 1, 1));
        }
    }

    /// <summary>UI 层实例：半透明条</summary>
    private sealed class UIBar : GameInstance
    {
        private readonly Vector2 _size;
        private readonly Vector4 _color;
        private readonly uint _tex;

        public UIBar(Vector2 pos, Vector2 size, Vector4 color, uint tex)
            : base(nameof(UIBar), new Vector2D(pos.X, pos.Y), LayerDepth.UI)
        {
            _size = size;
            _color = color;
            _tex = tex;
            LayerName = SceneAggregate.LayerNameUI;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var p = Transform.Position;
            batch.Draw(_tex, new Vector2(p.X - _size.X * 0.5f, p.Y - _size.Y * 0.5f),
                _size, _color, new Vector4(0, 0, 1, 1));
        }
    }
}
