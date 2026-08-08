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
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>
/// RenderPipeline 切片 · 可运行看效果 Demo（GameInstance 事件驱动版）。
///
/// 展示内容：
///   - Pass 1: SceneRenderPass → RT_Scene  (场景实例：背景方块 + 高亮光源)
///   - Pass 2: BloomPass  → RT_Bloom (后处理：只提取高亮区域做 9-tap 模糊)
///   - Pass 3: CompositorPass → 屏幕 (RT_Scene Opaque 打底 + RT_Bloom Additive 叠加)
///   - 左上角 1/4 视口放 Bloom 结果（分屏/小地图 PIP 演示）
///   - 鼠标移动高亮光源
///   - ESC 退出
/// </summary>
internal static class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static PostProcessShader? _bloomShader;
    private static BlitShader? _blitShader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;
    private static Camera2D? _camera;
    private static SceneAggregate? _scene;

    private static RenderTarget2D? _rtScene;
    private static RenderTarget2D? _rtBloom;
    private static RenderPipeline? _pipeline;

    private static void Main()
    {
        Console.WriteLine("=== RenderPipeline Visual Test ===");
        Console.WriteLine("  移动鼠标控制高亮光源 | ESC 退出");

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
        _bloomShader = new PostProcessShader(gl);
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

        _rtScene = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        _rtBloom = new RenderTarget2D(gl, vw, vh, withDepthStencil: false);

        // Pass 1: 画场景到 RT_Scene（SceneRenderPass 消费场景实例）
        var scenePass = new SceneRenderPass("ScenePass", gl, _scene, _camera, _rtScene);

        // Pass 2: 提取高亮 → Bloom 到 RT_Bloom
        _bloomShader.Use();
        _bloomShader.SetBrightnessThreshold(0.1f);
        _bloomShader.SetIntensity(1.8f);
        _bloomShader.SetTextureSize(vw, vh);
        var bloomPass = new PostProcessPass("BloomPass", gl, _bloomShader, _rtScene, _rtBloom);

        // Pass 3: 合成到屏幕
        var compositorPass = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);
        compositorPass.AddSource(_rtScene, ViewportRect.FullScreen, BlendState.Opaque);
        compositorPass.AddSource(_rtBloom, ViewportRect.FullScreen, BlendState.Additive);
        // 左上 1/4 单独展示 Bloom 结果（PIP 小地图演示）
        compositorPass.AddSource(_rtBloom, ViewportRect.TopLeftQuarter, BlendState.Opaque);

        _pipeline = new RenderPipeline(gl, vw, vh);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(bloomPass);
        _pipeline.AddPass(compositorPass);
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
        _pipeline!.Execute(ctx);
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
}
