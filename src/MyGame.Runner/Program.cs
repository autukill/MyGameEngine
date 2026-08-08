namespace MyGame.Runner;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.StencilMasking.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ToneMapping.Infrastructure;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>
/// GMS 风格动态效果 Demo：GameInstance 独立声明 Spotlight 与全场景 Bloom。
/// </summary>
internal sealed class Program
{
    private static EngineWindow? _window;
    private static SpriteShader? _spriteShader;
    private static BloomExtractShader? _bloomExtractShader;
    private static GaussianBlurShader? _bloomBlurShader;
    private static ToneMappingShader? _toneMappingShader;
    private static BlitShader? _blitShader;
    private static SpriteBatch? _batch;
    private static TextureLibrary? _textures;
    private static SpriteLibrary? _sprites;
    private static ContentPackageManager? _content;
    private static LoadedContentPackage? _package;
    private static SceneAggregate? _scene;
    private static Camera2D? _mainCamera;
    private static RenderTarget2D? _rtScene;
    private static RenderTargetPool? _targetPool;
    private static RenderPipeline? _pipeline;
    private static ScenePipelineBuilder? _pipelineBuilder;
    private static ViewportCompositorPass? _compositorPass;

    private static void Main(string[] args)
    {
        Console.WriteLine("=== Dynamic Render Effects Demo ===");
        Console.WriteLine("  4 个 OrbitingSprite 做圆周运动");
        Console.WriteLine("  鼠标位置 = Spotlight 中心 (Stencil ShowInside)");
        Console.WriteLine("  HDR Scene → Bloom → ACES Tone Mapping 由实例事件动态装配");
        Console.WriteLine("  ESC: 退出");

        _window = new EngineWindow(EngineWindowOptions.Default);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.OnDrawGUI += HandleDrawGUI;
        _window.OnResize += HandleResize;
        _window.OnClosing += HandleClosing;
        _window.Run();
    }

    private static void HandleLoad()
    {
        var gl = _window!.Graphics.Gl;
        var (width, height) = (_window.Width, _window.Height);

        _spriteShader = new SpriteShader(gl);
        _bloomExtractShader = new BloomExtractShader(gl);
        _bloomBlurShader = new GaussianBlurShader(gl);
        _toneMappingShader = new ToneMappingShader(gl);
        _blitShader = new BlitShader(gl);
        _batch = new SpriteBatch(gl) { DefaultShader = _spriteShader };
        _textures = new TextureLibrary(gl);
        _sprites = new SpriteLibrary(_textures);
        string assetsRoot = Path.Combine(AppContext.BaseDirectory, "AssetsCompiled");
        _content = new ContentPackageManager(_textures, _sprites, assetsRoot);
        _package = _content.Load("assets.json");
        var whiteTexture = _package.GetTexture("runner.white");
        var orbitingSprite = _package.GetSprite("runner.orbiting");
        _batch.SpriteResolver = _sprites;

        _scene = new SceneAggregate("MainScene")
        {
            ViewportWidth = width,
            ViewportHeight = height,
            Background = BackgroundConfig.FromColor(new Vector4(0.08f, 0.10f, 0.13f, 1f))
        };
        _scene.SetInput(_window.Input);
        _scene.SetSprites(_sprites);
        _scene.OnStart = () => Console.WriteLine($"[Scene] '{_scene.SceneName}' started.");

        _mainCamera = new Camera2D(new Vector2(width, height));
        _rtScene = new RenderTarget2D(gl, new RenderTargetDescriptor(
            width,
            height,
            RenderTargetColorFormat.Rgba16Float,
            RenderTargetDepthStencilFormat.Depth24Stencil8));
        _targetPool = new RenderTargetPool(gl);

        var scenePass = new SceneRenderPass("ScenePass", gl, _scene, _mainCamera, _rtScene);
        _compositorPass = new ViewportCompositorPass("CompositorPass", gl, _blitShader, _batch);

        _pipeline = new RenderPipeline(gl, width, height);
        _pipeline.AddPass(scenePass);
        _pipeline.AddPass(_compositorPass);

        _pipelineBuilder = new ScenePipelineBuilder(
            _pipeline,
            _compositorPass,
            _targetPool,
            width,
            height);
        _pipelineBuilder.RegisterRootSurface(RenderSurfaceKey.SceneColor, _rtScene);
        _pipelineBuilder.RegisterFactory(new StencilMaskEffectFactory(
            gl,
            _scene,
            _mainCamera,
            _spriteShader,
            whiteTexture,
            _textures,
            _sprites));
        _pipelineBuilder.RegisterFactory(new BloomEffectFactory(
            gl,
            _bloomExtractShader,
            _bloomBlurShader));
        _pipelineBuilder.RegisterFactory(new ToneMappingEffectFactory(
            gl,
            _toneMappingShader));

        var center = new Vector2D(width * 0.5f, height * 0.5f);
        var colors = new[]
        {
            new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
            new Vector4(0.3f, 1.0f, 0.3f, 1.0f),
            new Vector4(0.3f, 0.5f, 1.0f, 1.0f),
            new Vector4(1.0f, 1.0f, 0.3f, 1.0f)
        };
        for (int i = 0; i < colors.Length; i++)
        {
            _scene.Add(new OrbitingSprite(
                center,
                200f,
                i * MathF.PI / 2,
                colors[i],
                orbitingSprite));
        }

        _scene.Add(new SceneBloomController(
            _scene.RaiseEvent,
            new BloomSettings(0.3f, 1.5f, 1f, 2, BloomResolution.Half),
            ToneMappingSettings.Default));

        _scene.Add(new SpotlightController(
            _scene.RaiseEvent,
            center,
            120f,
            () => _window.NativeWindow.Close()));
    }

    private static void HandleStep(double deltaTime)
    {
        _scene!.PerformInput(_window!.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(deltaTime);
        _pipelineBuilder!.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    private static void HandleDraw()
    {
        var context = new RenderPassContext(
            _window!.Graphics.Gl,
            _spriteShader!,
            _batch!,
            _window.Width,
            _window.Height);
        _pipeline!.Execute(context);
    }

    private static void HandleDrawGUI()
    {
        if (_scene is null || _spriteShader is null || _batch is null || _window is null) return;
        _spriteShader.Use();
        _spriteShader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(
            0, _window.Width, _window.Height, 0, -1, 1));
        _batch.Begin();
        _scene.DrawGUI(_batch);
        _batch.End();
    }

    private static void HandleResize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_scene is not null)
        {
            _scene.ViewportWidth = width;
            _scene.ViewportHeight = height;
        }
        _mainCamera?.ResizeViewport(width, height);
        _rtScene?.Resize(width, height);
        _pipeline?.Resize(width, height);
        _pipelineBuilder?.Resize(width, height);
    }

    private static void HandleClosing()
    {
        _scene?.End();
        _pipelineBuilder?.Dispose();
        _pipeline?.Dispose();
        _targetPool?.Dispose();
        _rtScene?.Dispose();
        _package?.Dispose();
        _content?.Dispose();
        _textures?.Dispose();
        _batch?.Dispose();
        _blitShader?.Dispose();
        _toneMappingShader?.Dispose();
        _bloomBlurShader?.Dispose();
        _bloomExtractShader?.Dispose();
        _spriteShader?.Dispose();
    }
}
