namespace GameEngine.Hosting;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Presentation.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.StencilMasking.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.ToneMapping.Infrastructure;

internal sealed class Default2DGameRuntime : IDisposable
{
    private const string StencilWhiteTextureName = "__engine.hosting.stencil-white";

    private readonly EngineWindow _window;
    private readonly GameApplicationPlan _plan;
    private readonly Action _close;
    private readonly OwnedResourceStack _resources = new();
    private SpriteShader _spriteShader = null!;
    private SpriteBatch _batch = null!;
    private TextureLibrary _textures = null!;
    private SpriteLibrary _sprites = null!;
    private SceneAggregate? _scene;
    private Camera2D _camera = null!;
    private RenderTarget2D _sceneTarget = null!;
    private RenderTarget2D? _guiTarget;
    private RenderTargetPool _targetPool = null!;
    private RenderPipeline _pipeline = null!;
    private ScenePipelineBuilder _builder = null!;
    private bool _disposed;

    public Default2DGameContext Context { get; private set; } = null!;

    private Default2DGameRuntime(
        EngineWindow window,
        GameApplicationPlan plan,
        Action close)
    {
        _window = window;
        _plan = plan;
        _close = close;
    }

    public static Default2DGameRuntime Create(
        EngineWindow window,
        GameApplicationPlan plan,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        var runtime = new Default2DGameRuntime(window, plan, close);
        try
        {
            runtime.Initialize();
            return runtime;
        }
        catch
        {
            try { runtime.Dispose(); }
            catch { /* Preserve the initialization exception. */ }
            throw;
        }
    }

    public void Step(double deltaTime)
    {
        _scene!.PerformInput(_window.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(deltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    public void Draw() => _pipeline.Execute(new RenderPassContext(
        _window.Graphics.Gl,
        _spriteShader,
        _batch,
        _window.Width,
        _window.Height));

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _scene!.ViewportWidth = width;
        _scene.ViewportHeight = height;
        _camera.ResizeViewport(width, height);
        _sceneTarget.Resize(width, height);
        _guiTarget?.Resize(width, height);
        _pipeline.Resize(width, height);
        _builder.Resize(width, height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _scene?.End();
        }
        finally
        {
            _resources.Dispose();
        }
    }

    private void Initialize()
    {
        var gl = _window.Graphics.Gl;
        int width = _window.Width;
        int height = _window.Height;
        var renderer = _plan.Renderer;

        _spriteShader = _resources.Add(new SpriteShader(gl));
        StencilMaskShader? stencilShader = renderer.StencilMaskingEnabled
            ? _resources.Add(new StencilMaskShader(gl))
            : null;
        BloomExtractShader? bloomExtractShader = renderer.Bloom is not null
            ? _resources.Add(new BloomExtractShader(gl))
            : null;
        GaussianBlurShader? bloomBlurShader = renderer.Bloom is not null
            ? _resources.Add(new GaussianBlurShader(gl))
            : null;
        ToneMappingShader? toneMappingShader = renderer.HdrEnabled
            ? _resources.Add(new ToneMappingShader(gl))
            : null;
        var blitShader = _resources.Add(new BlitShader(gl));
        _batch = _resources.Add(new SpriteBatch(gl) { DefaultShader = _spriteShader });
        _textures = _resources.Add(new TextureLibrary(gl));
        _sprites = new SpriteLibrary(_textures);
        _batch.SpriteResolver = _sprites;

        TextureRef stencilWhite = default;
        if (renderer.StencilMaskingEnabled)
        {
            stencilWhite = _textures.RegisterRgba(
                StencilWhiteTextureName,
                1,
                1,
                new byte[] { 255, 255, 255, 255 },
                TextureSampler.PixelArt);
        }

        ContentPackageManager? contentManager = null;
        LoadedContentPackage? content = null;
        if (renderer.ContentPackagesRoot is { } configuredRoot)
        {
            string packagesRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(AppContext.BaseDirectory, configuredRoot));
            contentManager = _resources.Add(new ContentPackageManager(
                _textures,
                _sprites,
                packagesRoot));
            content = _resources.Add(contentManager.Load(renderer.ContentManifest!));
        }

        _scene = new SceneAggregate(_plan.SceneName)
        {
            ViewportWidth = width,
            ViewportHeight = height
        };
        _scene.SetInput(_window.Input);
        _scene.SetSprites(_sprites);
        _camera = new Camera2D(new Vector2(width, height));
        _sceneTarget = _resources.Add(new RenderTarget2D(gl, new RenderTargetDescriptor(
            width,
            height,
            renderer.HdrEnabled
                ? RenderTargetColorFormat.Rgba16Float
                : RenderTargetColorFormat.Rgba8,
            RenderTargetDepthStencilFormat.Depth24Stencil8)));
        if (renderer.SceneGuiEnabled)
        {
            _guiTarget = _resources.Add(new RenderTarget2D(gl, new RenderTargetDescriptor(
                width,
                height,
                RenderTargetColorFormat.Rgba8,
                RenderTargetDepthStencilFormat.None)));
        }
        _targetPool = _resources.Add(new RenderTargetPool(gl));
        _pipeline = _resources.Add(new RenderPipeline(gl, width, height));
        _pipeline.AddPass(new SceneRenderPass(
            "Hosting.Scene",
            gl,
            _scene,
            _camera,
            _sceneTarget));
        if (_guiTarget is not null)
        {
            _pipeline.AddPass(new SceneGuiRenderPass(
                "Hosting.SceneGui",
                gl,
                _scene,
                _guiTarget));
        }

        _builder = _resources.Add(new ScenePipelineBuilder(
            _pipeline,
            _targetPool,
            width,
            height));
        _builder.RegisterRootSurface(
            RenderSurfaceKey.SceneColor,
            _sceneTarget,
            renderer.HdrEnabled
                ? RenderSurfaceEncoding.Linear
                : RenderSurfaceEncoding.Display);
        if (_guiTarget is not null)
            _builder.RegisterRootSurface(RenderSurfaceKey.SceneGui, _guiTarget);
        if (renderer.StencilMaskingEnabled)
        {
            _builder.RegisterFactory(new StencilMaskEffectFactory(
                gl,
                _scene,
                _camera,
                _spriteShader,
                stencilShader!,
                stencilWhite,
                _textures,
                _sprites));
        }
        if (renderer.Bloom is not null)
            _builder.RegisterFactory(new BloomEffectFactory(
                gl,
                bloomExtractShader!,
                bloomBlurShader!));
        if (renderer.HdrEnabled)
            _builder.RegisterFactory(new ToneMappingEffectFactory(gl, toneMappingShader!));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, blitShader, _batch));

        Context = new Default2DGameContext(
            _window,
            _scene,
            _textures,
            _sprites,
            content,
            _camera,
            _pipeline,
            _builder,
            _targetPool,
            _close);
        _plan.ConfigureScene(Context);
        _scene.Add(new DefaultWorldPresentationController(_scene.RaiseEvent, renderer));
        if (renderer.SceneGuiEnabled)
            _scene.Add(new DefaultGuiPresentationController(_scene.RaiseEvent));
    }
}
