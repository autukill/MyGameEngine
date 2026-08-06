namespace RenderPipeline.VisualTests;

using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>
/// RenderPipeline 切片 · 可运行看效果 Demo。
///
/// 展示内容：
///   - Pass 1: ScenePass → RT_Scene  (画彩色方块 + 高亮光源，验证 SpriteBatch/Shader)
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

    private static RenderTarget2D? _rtScene;
    private static RenderTarget2D? _rtBloom;
    private static RenderPipeline? _pipeline;
    private static ViewportCompositorPass? _compositorPass;

    private static Vector2 _lightScreen = new(640, 360);
    private static float _animTime;

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
        _white = new WhiteTexture(gl);
        _camera = new Camera2D(new Vector2(vw, vh));

        _rtScene = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        _rtBloom = new RenderTarget2D(gl, vw, vh, withDepthStencil: false);

        // Pass 1: 画基础场景到 RT_Scene
        var scenePass = new SimpleScenePass("ScenePass", gl, _camera, _white, _rtScene);
        // Pass 2: 提取高亮 → Bloom 到 RT_Bloom
        var bloomPass = new PostProcessPass("BloomPass", gl, _bloomShader, _rtScene, _rtBloom);
        _bloomShader.Use();
        _bloomShader.SetBrightnessThreshold(0.1f);
        _bloomShader.SetIntensity(1.8f);
        _bloomShader.SetTextureSize(vw, vh);

        // Pass 3: 合成到屏幕
        _compositorPass = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);
        _compositorPass.AddSource(_rtScene, ViewportRect.FullScreen, BlendState.Opaque);
        _compositorPass.AddSource(_rtBloom, ViewportRect.FullScreen, BlendState.Additive);
        // 左上 1/4 单独展示 Bloom 结果（PIP 小地图演示）
        _compositorPass.AddSource(_rtBloom, ViewportRect.TopLeftQuarter, BlendState.Opaque);

        _pipeline = new RenderPipeline(gl, vw, vh);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(bloomPass);
        _pipeline.AddPass(_compositorPass);

        try
        {
            var input = _window.NativeWindow.CreateInput();
            foreach (var mouse in input.Mice)
                mouse.MouseMove += (_, pos) => _lightScreen = new Vector2(pos.X, pos.Y);
            foreach (var keyboard in input.Keyboards)
                keyboard.KeyDown += (_, key, _) =>
                {
                    if (key == Key.Escape) _window.NativeWindow.Close();
                };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Input] WARN: {ex.Message}");
        }
    }

    private static void HandleStep(double dt) => _animTime += (float)dt;

    private static void HandleDraw()
    {
        var gl = _window!.Graphics.Gl;
        var ctx = new RenderPassContext(gl, _spriteShader!, _batch!,
            _window.Width, _window.Height);
        _pipeline!.Execute(ctx);
    }

    /// <summary>
    /// 内部自定义基础场景 Pass：画背景方块 + 高亮光源（仅演示 RenderPipeline 的 Pass 机制）。
    /// </summary>
    private sealed class SimpleScenePass : RenderPass
    {
        private readonly GL _gl;
        private readonly Camera2D _camera;
        private readonly WhiteTexture _white;
        private readonly RenderTarget2D _target;

        public override RenderTarget2D? Output => _target;
        public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();

        public SimpleScenePass(string name, GL gl, Camera2D camera,
            WhiteTexture white, RenderTarget2D target) : base(name)
        {
            _gl = gl;
            _camera = camera;
            _white = white;
            _target = target;
        }

        public override void Execute(in RenderPassContext ctx)
        {
            _gl.ClearColor(0.08f, 0.09f, 0.12f, 1.0f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            ctx.DefaultShader.Use();
            ctx.DefaultShader.SetProjection(_camera.GetViewProjectionMatrix());

            ctx.Batch.Begin();

            // 背景装饰方块（暗色）
            for (int i = 0; i < 8; i++)
            {
                float x = 60 + i * 90;
                var color = new Vector4(0.18f, 0.20f, 0.28f, 1f);
                ctx.Batch.Draw(_white.Handle, new Vector2(x, 60), new Vector2(70, 70), color);
                ctx.Batch.Draw(_white.Handle, new Vector2(x, 160), new Vector2(70, 70), color);
            }

            // 高亮光源（跟随鼠标 + 动画脉动）
            var pulse = 0.6f + 0.4f * MathF.Sin(_animTime * 3f);
            var lightColor = new Vector4(pulse, pulse, 1f, 1f);
            ctx.Batch.Draw(_white.Handle, _lightScreen - new Vector2(30, 30),
                new Vector2(60, 60), lightColor);

            // 光源外圈（稍亮，用于 Bloom 提取）
            ctx.Batch.Draw(_white.Handle, _lightScreen - new Vector2(46, 46),
                new Vector2(92, 92), new Vector4(pulse * 0.5f, pulse * 0.5f, 0.8f, 1f));

            ctx.Batch.End();
        }
    }
}
