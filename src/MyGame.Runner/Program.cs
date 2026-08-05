namespace MyGame.Runner;

using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.StencilMasking.Infrastructure;

/// <summary>
/// Phase 1.3 Demo (GMS-style): Stencil 遮罩 + Bloom 后处理 + 多 Pass 合成。
///
/// 所有游戏逻辑封装在 GameInstance 子类（OrbitingSprite / BackgroundSprite）中，
/// Program.cs 只负责：引擎初始化 + 场景装配 + 每帧调度。
///
/// 渲染流程:
///   Pass 1: SceneRenderPass -> RT_Scene    (所有实例的 OnDraw)
///   Pass 2: StencilMaskPass  -> RT_Masked (圆圈 Stencil + 重绘 OnDraw)
///   Pass 3: PostProcessPass  -> RT_Bloom  (Bright+Blur)
///   Pass 4: ViewportCompositorPass -> 屏幕 (RT_Scene + RT_Bloom 叠加)
///
/// 鼠标移动闪光灯; ESC 退出。
/// </summary>
internal sealed class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static PostProcessShader? _bloomShader;
    private static BlitShader? _blitShader;
    private static SpriteBatch? _batch;
    private static WhiteTexture? _white;

    private static SceneAggregate? _scene;
    private static Camera2D? _mainCamera;

    private static RenderTarget2D? _rtScene;
    private static RenderTarget2D? _rtMasked;
    private static RenderTarget2D? _rtBloom;

    private static RenderPipeline? _pipeline;
    private static SceneRenderPass? _scenePass;
    private static StencilMaskPass? _stencilPass;
    private static PostProcessPass? _bloomPass;
    private static ViewportCompositorPass? _compositorPass;

    private static Vector2 _mouseScreen = new(640, 360);

    private static void Main(string[] args)
    {
        Console.WriteLine("=== Phase 1.3 GMS-style Demo ===");
        Console.WriteLine("  4 个 OrbitingSprite 做圆周运动");
        Console.WriteLine("  鼠标位置 = 聚光灯圆心 (Stencil ShowInside)");
        Console.WriteLine("  Bloom: 圆圈内高亮区域发光");
        Console.WriteLine("  ESC:   退出");

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

        // 1. 共享资源
        _spriteShader = new SpriteShader(gl);
        _bloomShader = new PostProcessShader(gl);
        _blitShader = new BlitShader(gl);
        _batch = new SpriteBatch(gl);
        _white = new WhiteTexture(gl);

        // 2. 场景（DDD 聚合根，非旧的 SceneRenderContext）
        _scene = new SceneAggregate(sceneName: "MainScene");

        // 3. 相机（独立于场景，被 Pass 消费）
        _mainCamera = new Camera2D(new Vector2(vw, vh));

        // 4. 三张 RenderTarget
        _rtScene = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        _rtMasked = new RenderTarget2D(gl, vw, vh, withDepthStencil: true);
        _rtBloom = new RenderTarget2D(gl, vw, vh, withDepthStencil: false);

        // 5. 渲染管道（DAG）
        _scenePass = new SceneRenderPass("ScenePass", _scene, _mainCamera, _rtScene);
        _stencilPass = new StencilMaskPass("StencilMaskPass", gl, _scene, _mainCamera,
            _rtMasked, _spriteShader, _white);
        _bloomPass = new PostProcessPass("BloomPass", gl, _bloomShader, _rtMasked, _rtBloom);
        _compositorPass = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);

        _pipeline = new RenderPipeline(gl, vw, vh);
        _pipeline.AddPass(_scenePass);
        _pipeline.AddPass(_stencilPass);
        _pipeline.AddPass(_bloomPass);
        _pipeline.AddPass(_compositorPass);

        // 6. 合成 Pass 源：RT_Scene 不透明底 + RT_Bloom 叠加
        _compositorPass.AddSource(_rtScene, ViewportRect.FullScreen, BlendState.Opaque);
        _compositorPass.AddSource(_rtBloom, ViewportRect.FullScreen, BlendState.Additive);

        // 7. Bloom 参数
        _bloomShader.Use();
        _bloomShader.SetBrightnessThreshold(0.3f);
        _bloomShader.SetIntensity(1.5f);
        _bloomShader.SetTextureSize(_rtMasked.Width, _rtMasked.Height);

        // 8. 装配场景（GMS 风格：创建实例加入场景）
        var center = new Vector2D(vw * 0.5f, vh * 0.5f);
        var colors = new[] {
            new Vector4(1.0f, 0.3f, 0.3f, 1.0f),   // Red
            new Vector4(0.3f, 1.0f, 0.3f, 1.0f),   // Green
            new Vector4(0.3f, 0.5f, 1.0f, 1.0f),   // Blue
            new Vector4(1.0f, 1.0f, 0.3f, 1.0f),   // Yellow
        };

        // 背景
        _scene.Add(new BackgroundSprite(
            _white.Handle,
            new Vector2(vw, vh),
            new Vector4(0.08f, 0.10f, 0.13f, 1.0f)));

        // 4 个圆周运动精灵
        for (int i = 0; i < 4; i++)
        {
            _scene.Add(new OrbitingSprite(
                center: center,
                radius: 200f,
                phase: i * MathF.PI / 2,
                color: colors[i],
                textureHandle: _white.Handle));
        }

        // 9. 输入
        SetupInput(gl);
    }

    private static void SetupInput(GL gl)
    {
        try
        {
            var input = _window!.NativeWindow.CreateInput();
            foreach (var mouse in input.Mice)
            {
                mouse.MouseMove += (m, pos) =>
                {
                    _mouseScreen = new Vector2(pos.X, pos.Y);
                };
            }
            foreach (var keyboard in input.Keyboards)
            {
                keyboard.KeyDown += (kb, key, scancode) =>
                {
                    if (key == Key.Escape)
                        _window.NativeWindow.Close();
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Input] WARN: {ex.Message}");
        }
    }

    private static void HandleStep(double dt)
    {
        // GMS-style: 只调用场景的 Step 调度器，由聚合根遍历实例调用 OnStep
        _scene!.PerformStep(dt);
    }

    private static void HandleDraw()
    {
        // 闪光灯圆心 = 鼠标位置，每帧更新 StencilMaskPass 的遮罩圆
        _stencilPass!.SetMaskCircle(_mouseScreen, 120f);

        var ctx = new RenderPassContext(
            _window!.Graphics.Gl, _spriteShader!, _batch!, _window.Width, _window.Height);

        _pipeline!.Execute(ctx);
    }
}
