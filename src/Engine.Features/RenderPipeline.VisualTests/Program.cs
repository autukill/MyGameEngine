namespace RenderPipeline.VisualTests;

using System.Numerics;
using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Bloom.Application;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>
/// RenderPipeline 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：
///   - Pass 1: SceneRenderPass → RT_Scene  (场景实例：背景方块 + 高亮光源)
///   - Pass 2: BloomPass 内部执行 Bright + Horizontal/Vertical Ping-Pong
///   - Pass 3: CompositorPass → 屏幕 (RT_Scene Opaque 打底 + RT_Bloom Additive 叠加)
///   - 鼠标移动高亮光源
///   - ESC 退出
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static BloomExtractShader? _extractShader;
    private static GaussianBlurShader? _blurShader;
    private static BlitShader? _blitShader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static SceneAggregate? _scene;

    private static RenderTarget2D? _rtScene;
    private static RenderTargetPool? _targetPool;
    private static RenderPipeline? _pipeline;
    private static ScenePipelineBuilder? _builder;

    private static void Main()
    {
        Console.WriteLine("=== RenderPipeline Visual Test ===");
        Console.WriteLine("  移动鼠标控制高亮光源 | ESC 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
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
        var (vw, vh) = (_window.Width, _window.Height);

        _spriteShader = new SpriteShader(gl);
        _extractShader = new BloomExtractShader(gl);
        _blurShader = new GaussianBlurShader(gl);
        _blitShader = new BlitShader(gl);
        _batch = new SpriteBatch(gl);
        _batch.DefaultShader = _spriteShader;
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(vw, vh));

        // 场景实例化：背景装饰 + 高亮光源（业务逻辑全部在 GameInstance 内）
        _scene = new SceneAggregate("BloomDemo");
        _scene.SetInput(_window.Input);
        _scene.ViewportWidth = vw;
        _scene.ViewportHeight = vh;
        _scene.Background = BackgroundConfig.FromColor(new Vector4(0.08f, 0.09f, 0.12f, 1f));

        for (int i = 0; i < 8; i++)
        {
            _scene.Add(new BackgroundDecor(_white.Handle,
                new Vector2(60 + i * 90, 60), new Vector4(0.18f, 0.20f, 0.28f, 1f)));
            _scene.Add(new BackgroundDecor(_white.Handle,
                new Vector2(60 + i * 90, 160), new Vector4(0.18f, 0.20f, 0.28f, 1f)));
        }
        _scene.Add(new LightSource(_white.Handle, _window));
        _scene.Add(new BloomController(
            _scene.RaiseEvent,
            new BloomSettings(0.1f, 1.8f, 1.25f, 2, BloomResolution.Half)));

        _rtScene = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        _targetPool = new RenderTargetPool(gl);

        // Pass 1: 画场景到 RT_Scene（SceneRenderPass 消费场景实例）
        var scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera, _rtScene);

        // Bloom Pass 由 ScenePipelineBuilder 根据实例请求动态装配。
        var compositorPass = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);
        compositorPass.AddSource(_rtScene, ViewportRect.FullScreen, BlendState.Opaque);

        _pipeline = new RenderPipeline(gl, vw, vh);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(compositorPass);
        _builder = new ScenePipelineBuilder(_pipeline, compositorPass, _targetPool, vw, vh);
        _builder.RegisterFactory(new BloomEffectFactory(
            gl, _rtScene, _extractShader, _blurShader));
    }

    private static void HandleStep(double dt)
    {
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(dt);
        _builder!.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    private static void HandleDraw()
    {
        var ctx = new RenderPassContext(
            _window!.Graphics.Gl, _spriteShader!, _batch!,
            _window.Width, _window.Height);
        _pipeline!.Execute(ctx);
    }

    private static void HandleResize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _scene!.ViewportWidth = width;
        _scene.ViewportHeight = height;
        _camera!.ResizeViewport(width, height);
        _rtScene!.Resize(width, height);
        _pipeline!.Resize(width, height);
        _builder!.Resize(width, height);
    }

    private static void HandleClosing()
    {
        _scene?.End();
        _builder?.Dispose();
        _pipeline?.Dispose();
        _targetPool?.Dispose();
        _rtScene?.Dispose();
        _white?.Dispose();
        _batch?.Dispose();
        _blitShader?.Dispose();
        _blurShader?.Dispose();
        _extractShader?.Dispose();
        _spriteShader?.Dispose();
    }

    /// <summary>背景装饰方块（Background 层）</summary>
    private sealed class BackgroundDecor : GameInstance
    {
        private readonly uint _tex;
        private readonly Vector4 _color;

        public BackgroundDecor(uint tex, Vector2 pos, Vector4 color)
            : base(nameof(BackgroundDecor), new Vector2D(pos.X, pos.Y), LayerDepth.Background)
        {
            _tex = tex;
            _color = color;
            LayerName = SceneAggregate.LayerNameBackground;
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var p = Transform.Position;
            batch.Draw(_tex, new Vector2(p.X - 35, p.Y - 35), new Vector2(70, 70), _color,
                new Vector4(0, 0, 1, 1));
        }
    }

    /// <summary>
    /// 高亮光源（Instances 层）：OnStep 读鼠标位置 + 动画脉动，OnDraw 画高亮与外圈。
    /// 亮度超过 Bloom 阈值 → 被 BloomPass 提取。
    /// </summary>
    private sealed class LightSource : GameInstance
    {
        private readonly uint _tex;
        private readonly EngineWindow _window;
        private Vector2 _pos;
        private float _t;

        public LightSource(uint tex, EngineWindow window)
            : base(nameof(LightSource), new Vector2D(640, 360), LayerDepth.Instances)
        {
            _tex = tex;
            _window = window;
            _pos = new Vector2(640, 360);
            LayerName = SceneAggregate.LayerNameInstances;
        }

        public override void OnStep(double dt)
        {
            _t += (float)dt;
            if (Input is not null)
                _pos = new Vector2(Input.MousePosition.X, Input.MousePosition.Y);
        }

        public override void OnDraw(ISpriteBatch batch)
        {
            var pulse = 0.6f + 0.4f * MathF.Sin(_t * 3f);
            // 光源外圈（稍亮，用于 Bloom 提取）
            batch.Draw(_tex, _pos - new Vector2(46, 46), new Vector2(92, 92),
                new Vector4(pulse * 0.5f, pulse * 0.5f, 0.8f, 1f), new Vector4(0, 0, 1, 1));
            // 高亮光源（跟随鼠标 + 动画脉动）
            batch.Draw(_tex, _pos - new Vector2(30, 30), new Vector2(60, 60),
                new Vector4(pulse, pulse, 1f, 1f), new Vector4(0, 0, 1, 1));
        }

        public override void OnKeyDown(InputKey key)
        {
            if (key == InputKey.Escape) _window.NativeWindow.Close();
        }
    }

    private sealed class BloomController : GameInstance
    {
        private readonly Action<GameEngine.Core.Domain.Events.IDomainEvent> _raiseEvent;
        private readonly BloomSettings _settings;

        public BloomController(
            Action<GameEngine.Core.Domain.Events.IDomainEvent> raiseEvent,
            BloomSettings settings)
        {
            _raiseEvent = raiseEvent;
            _settings = settings;
        }

        public override void OnCreate() => this.RequestBloom(_settings, _raiseEvent);
        public override void OnDestroy() => this.ReleaseBloom(_raiseEvent);
    }
}
