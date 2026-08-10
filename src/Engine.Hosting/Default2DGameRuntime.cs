namespace GameEngine.Hosting;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Bloom.Infrastructure;
using GameEngine.Features.Animation;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.ShaderAssets.Domain;
using GameEngine.Features.Presentation.Infrastructure;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.StencilMasking.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TextRendering.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ToneMapping.Infrastructure;
using GameEngine.Features.TransformHierarchy.Gameplay;

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
    private TextRuntime _text = null!;
    private SpriteLibrary _sprites = null!;
    private AnimationLibrary _animations = null!;
    private SceneTransformRuntime _transforms = null!;
    private ShaderLibrary _shaders = null!;
    private SceneAggregate? _scene;
    private Camera2D _camera = null!;
    private RenderTarget2D _sceneTarget = null!;
    private RenderTarget2D? _guiTarget;
    private RenderTargetPool _targetPool = null!;
    private RenderPipeline _pipeline = null!;
    private ScenePipelineBuilder _builder = null!;
    private PerformanceTelemetrySampler? _performanceTelemetry;
    private ContentHotReloadCoordinator? _contentHotReload;
    private ShaderHotReloadCoordinator? _shaderHotReload;
    private SceneNavigator _scenes = null!;
    private readonly List<RenderView> _renderViews = [];
    private LogicalInputRecorder? _inputRecorder;
    private LogicalInputPlayback? _inputPlayback;
    private GameplayStateRecorder? _stateRecorder;
    private GameplayStateVerifier? _stateVerifier;
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
        ulong nextStepIndex = _scene!.Clock.StepIndex == ulong.MaxValue
            ? throw new InvalidOperationException("Simulation Step index overflowed.")
            : _scene.Clock.StepIndex + 1UL;
        if (_inputRecorder is not null)
            _inputRecorder.BeginStep(nextStepIndex, _plan.InputMap, _window.Input);
        else if (_inputPlayback is not null)
            _inputPlayback.BeginStep(nextStepIndex);
        else
            _scene.PerformInput(_window.Input.KeysPressed, _window.Input.KeysReleased);
        _scene.PerformStep(deltaTime);
        for (int i = 0; i < _renderViews.Count; i++)
            _renderViews[i].Camera.Update(deltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        ApplyPendingSceneSwitch();
        _transforms.Synchronize();
        CaptureOrVerifyGameplayState();
        _contentHotReload?.Tick();
        _shaderHotReload?.Tick();
    }

    public void Draw() => _pipeline.Execute(new RenderPassContext(
        _window.Graphics.Gl,
        _spriteShader,
        _batch,
        _window.Width,
        _window.Height,
        _window.FrameStatisticsSink));

    public void SamplePerformance() => _performanceTelemetry?.Tick();

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        _scene!.ViewportWidth = width;
        _scene.ViewportHeight = height;
        for (int i = 0; i < _renderViews.Count; i++)
        {
            RenderView view = _renderViews[i];
            var (renderWidth, renderHeight) = RenderViewLayoutBuilder.ResolveRenderSize(
                view.Viewport,
                view.RenderScale,
                width,
                height);
            view.Camera.ResizeViewport(renderWidth, renderHeight);
            view.Target.Resize(renderWidth, renderHeight);
        }
        _guiTarget?.Resize(width, height);
        _pipeline.Resize(width, height);
        _builder.Resize(_sceneTarget.Width, _sceneTarget.Height);
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
        BloomExtractShader? bloomExtractShader = renderer.AnyBloomEnabled
            ? _resources.Add(new BloomExtractShader(gl))
            : null;
        GaussianBlurShader? bloomBlurShader = renderer.AnyBloomEnabled
            ? _resources.Add(new GaussianBlurShader(gl))
            : null;
        ToneMappingShader? toneMappingShader = renderer.AnyHdrEnabled
            ? _resources.Add(new ToneMappingShader(gl))
            : null;
        var blitShader = _resources.Add(new BlitShader(gl));
        _batch = _resources.Add(new SpriteBatch(gl)
        {
            DefaultShader = _spriteShader,
            Statistics = _window.FrameStatisticsSink
        });
        _textures = _resources.Add(new TextureLibrary(gl));
        _text = _resources.Add(new TextRuntime(_textures));
        _sprites = new SpriteLibrary(_textures);
        _animations = new AnimationLibrary();
        _transforms = _resources.Add(new SceneTransformRuntime());
        _shaders = _resources.Add(new ShaderLibrary(gl));
        _batch.SpriteResolver = _sprites;
        _batch.ShaderResolver = _shaders;

        ShaderFileSetSnapshot? shaderSnapshot = null;
        string? shaderRoot = null;
        if (renderer.ShaderFiles is { Count: > 0 } shaderFiles)
        {
            string configuredShaderRoot = renderer.ShaderRoot!;
            shaderRoot = Path.GetFullPath(Path.IsPathRooted(configuredShaderRoot)
                ? configuredShaderRoot
                : Path.Combine(AppContext.BaseDirectory, configuredShaderRoot));
            shaderSnapshot = ShaderFileSetReader.Read(shaderRoot, shaderFiles);
            foreach (ShaderProgramSource source in shaderSnapshot.Sources)
                _shaders.Create(source);
            if (renderer.ShaderMaterials is { Count: > 0 } materials)
            {
                foreach (MaterialAssetDefinition material in materials)
                    RegisterMaterial(_shaders, material);
            }
        }

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
                _animations,
                packagesRoot));
            content = _resources.Add(renderer.ContentPackage is { } package
                ? contentManager.Load(package)
                : contentManager.Load(renderer.ContentManifest!));
        }

        _scene = new SceneAggregate(_plan.InitialScene.Name)
        {
            ViewportWidth = width,
            ViewportHeight = height
        };
        if (_plan.InputRecorder is { } recorder)
        {
            recorder.Prepare(_plan.InputMap, _plan.WindowOptions.FixedDeltaTime!.Value);
            _inputRecorder = recorder;
            _scene.SetInput(recorder);
        }
        else if (_plan.InputPlayback is { } recording)
        {
            _inputPlayback = new LogicalInputPlayback(recording, _plan.InputMap);
            _scene.SetInput(_inputPlayback);
        }
        else
        {
            _scene.SetInput(_window.Input);
        }
        _scene.SetInputMap(_plan.InputMap);
        if (_plan.StateRecorder is { } stateRecorder)
        {
            stateRecorder.Prepare(_plan.WindowOptions.FixedDeltaTime!.Value);
            _stateRecorder = stateRecorder;
        }
        else if (_plan.StateVerifier is { } stateVerifier)
        {
            _stateVerifier = stateVerifier;
        }
        _scene.SetSprites(_sprites);
        _scene.SetInstanceFactory(_plan.Instances);
        _scene.SetGameplayQueryStatisticsEnabled(renderer.PerformanceTelemetry is not null);
        _scenes = new SceneNavigator(_plan.Scenes, _plan.InitialSceneActivation);
        _scene.SetSceneSwitchRequester(_scenes);
        IReadOnlyList<RenderViewDefinition> viewDefinitions = renderer.RenderViews ??
            Array.AsReadOnly(new[]
            {
                new RenderViewDefinition(
                    RenderViewRef.Main,
                    ViewportRect.FullScreen,
                    ViewportFitMode.Stretch,
                    1f,
                    0,
                    SceneLayerFilter.All,
                    renderer.MainEffects,
                    null,
                    0)
            });
        for (int i = 0; i < viewDefinitions.Count; i++)
        {
            RenderViewDefinition definition = viewDefinitions[i];
            var (renderWidth, renderHeight) = RenderViewLayoutBuilder.ResolveRenderSize(
                definition.Viewport,
                definition.RenderScale,
                width,
                height);
            var camera = new Camera2D(new Vector2(renderWidth, renderHeight));
            var target = _resources.Add(new RenderTarget2D(gl, new RenderTargetDescriptor(
                renderWidth,
                renderHeight,
                definition.Effects.IsHdr
                    ? RenderTargetColorFormat.Rgba16Float
                    : RenderTargetColorFormat.Rgba8,
                RenderTargetDepthStencilFormat.Depth24Stencil8)));
            _renderViews.Add(new RenderView(definition, camera, target));
        }
        _camera = _renderViews[0].Camera;
        _sceneTarget = _renderViews[0].Target;
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
        for (int i = 0; i < _renderViews.Count; i++)
        {
            RenderView view = _renderViews[i];
            var scenePass = new SceneRenderPass(
                $"Hosting.Scene:{view.Ref}",
                gl,
                _scene,
                view.Camera,
                view.Target,
                view.SceneLayers,
                measureTiming: _window.FrameStatisticsSink is not null);
            view.AttachScenePass(scenePass);
            _pipeline.AddPass(scenePass);
        }
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
            _sceneTarget.Width,
            _sceneTarget.Height));
        for (int i = 0; i < _renderViews.Count; i++)
        {
            RenderView view = _renderViews[i];
            _builder.RegisterRootSurface(
                view.SceneColor,
                view.Target,
                view.Effects.IsHdr
                    ? RenderSurfaceEncoding.Linear
                    : RenderSurfaceEncoding.Display);
        }
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
                _sprites,
                _shaders,
                _renderViews[0].SceneLayers));
        }
        if (renderer.AnyBloomEnabled)
            _builder.RegisterFactory(new BloomEffectFactory(
                gl,
                bloomExtractShader!,
                bloomBlurShader!));
        if (renderer.AnyHdrEnabled)
            _builder.RegisterFactory(new ToneMappingEffectFactory(gl, toneMappingShader!));
        _builder.RegisterFactory(new PresentationEffectFactory(gl, blitShader, _batch));

        Context = new Default2DGameContext(
            _window,
            _scene,
            _textures,
            _sprites,
            _animations,
            _transforms,
            _text,
            _shaders,
            content,
            _camera,
            _pipeline,
            _builder,
            _targetPool,
            _sceneTarget,
            _guiTarget,
            renderer.ResolvedViewports,
            _renderViews,
            _scenes,
            _plan.Instances,
            _plan.InputMap,
            _close);
        if (renderer.ShaderHotReload is { } shaderHotReload)
        {
            _shaderHotReload = _resources.Add(new ShaderHotReloadCoordinator(
                _shaders,
                shaderRoot!,
                renderer.ShaderFiles!,
                shaderSnapshot!,
                shaderHotReload));
        }
        if (renderer.ContentHotReload is { } hotReload)
        {
            ContentPackageRef package = renderer.ContentPackage ?? new ContentPackageRef(
                content!.Id,
                renderer.ContentManifest!);
            _contentHotReload = _resources.Add(new ContentHotReloadCoordinator(
                contentManager!,
                package,
                hotReload));
        }
        if (renderer.PerformanceTelemetry is { } telemetry)
        {
            _performanceTelemetry = new PerformanceTelemetrySampler(
                telemetry,
                () => Context.CapturePerformanceSnapshot(
                    telemetry.Budget,
                    resetGameplayQueryStatistics: true));
        }
        ConfigureScene(_plan.InitialSceneActivation);
    }

    private void ApplyPendingSceneSwitch()
    {
        if (!_scenes.TryTakePending(out ISceneActivation next)) return;

        _scene!.TransitionTo(next.Scene.Name);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        _scenes.Commit(next.Scene);
        ConfigureScene(next);
        _scene.Start();
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
    }

    private void CaptureOrVerifyGameplayState()
    {
        if (_stateRecorder is null && _stateVerifier is null) return;
        GameplayStateSnapshot snapshot = _scene!.CaptureGameplayState();
        if (_stateRecorder is not null)
        {
            _stateRecorder.Capture(snapshot);
            return;
        }
        if (!_stateVerifier!.Verify(snapshot))
            throw new GameplayStateDivergenceException(_stateVerifier.FirstDivergence!);
        if (_plan.CloseOnReplayCompletion &&
            _stateVerifier.IsComplete &&
            _inputPlayback is { IsComplete: true })
        {
            _close();
        }
    }

    private void ConfigureScene(ISceneActivation activation)
    {
        _scenes.GetDefinition(activation.Scene).Configure(Context, activation);
        var renderer = _plan.Renderer;
        SceneAggregate scene = _scene!;
        if (renderer.MultipleRenderViewsEnabled)
        {
            for (int i = 0; i < Context.RenderViews.Count; i++)
            {
                RenderView view = Context.RenderViews[i];
                if (view.Effects.IsHdr)
                    scene.Add(new DefaultWorldEffectsController(
                        scene.RaiseEvent,
                        view.Ref,
                        view.SceneColor,
                        view.Effects));
                Context.PresentViewSurface(
                    view.Ref,
                    view.DisplayColor,
                    layer: 0,
                    blend: PresentationBlendMode.Opaque);
            }
            if (renderer.SceneGuiEnabled)
                scene.Add(new DefaultGuiPresentationController(scene.RaiseEvent));
            return;
        }
        RenderView mainView = Context.RenderViews[0];
        if (mainView.Effects.IsHdr)
            scene.Add(new DefaultWorldEffectsController(
                scene.RaiseEvent,
                mainView.Ref,
                mainView.SceneColor,
                mainView.Effects));
        Context.PresentWorldSurface(
            mainView.DisplayColor,
            layer: 0,
            blend: PresentationBlendMode.Opaque);
        if (_plan.Renderer.SceneGuiEnabled)
            scene.Add(new DefaultGuiPresentationController(scene.RaiseEvent));
    }

    private static void RegisterMaterial(
        ShaderLibrary shaders,
        MaterialAssetDefinition definition)
    {
        var material = shaders.CreateMaterial(
            definition.Name,
            new ShaderRef(definition.Shader),
            definition.Uniforms.Select(item => item.Uniform).ToArray());
        foreach (MaterialUniformAssetDefinition uniform in definition.Uniforms)
        {
            MaterialUniformDefaultValue value = uniform.DefaultValue;
            switch (uniform.Uniform.Type)
            {
                case ShaderUniformType.Float:
                    material.SetFloat(uniform.Uniform.Name, value.FloatValue);
                    break;
                case ShaderUniformType.Int:
                    material.SetInt(uniform.Uniform.Name, value.IntValue);
                    break;
                case ShaderUniformType.Vector2:
                    material.SetVector2(uniform.Uniform.Name, value.Vector2Value);
                    break;
                case ShaderUniformType.Vector4:
                    material.SetVector4(uniform.Uniform.Name, value.Vector4Value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported material uniform type '{uniform.Uniform.Type}'.");
            }
        }
    }

}
