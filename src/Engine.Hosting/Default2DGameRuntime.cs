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
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.OpenAL;
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
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TextRendering.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ToneMapping.Infrastructure;
using GameEngine.Features.TransformHierarchy.Gameplay;

internal sealed class Default2DGameRuntime : IDisposable
{
    private const string StencilWhiteTextureName = "__engine.hosting.stencil-white";
    private const int MaximumViewportPointers = 16;

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
    private AudioLibrary _audioClips = null!;
    private AudioRuntime? _audio;
    private SceneAudioScope _sceneAudio = null!;
    private SceneTransformRuntime _transforms = null!;
    private ShaderLibrary _shaders = null!;
    private SceneAggregate? _scene;
    private Camera2D _camera = null!;
    private readonly TileSetLibrary _tileSets = new();
    private readonly TileMapLibrary _tileMaps = new();
    private readonly TileMapRenderer _tileMapRenderer;
    private RenderTarget2D _sceneTarget = null!;
    private RenderTarget2D? _guiTarget;
    private RenderTargetPool _targetPool = null!;
    private RenderPipeline _pipeline = null!;
    private ScenePipelineBuilder _builder = null!;
    private PerformanceTelemetrySampler? _performanceTelemetry;
    private ContentHotReloadCoordinator? _contentHotReload;
    private ContentPackageManager? _contentManager;
    private LoadedContentPackage? _globalContent;
    private LoadedContentPackage? _sceneContent;
    private ShaderHotReloadCoordinator? _shaderHotReload;
    private SceneNavigator _scenes = null!;
    private readonly List<RenderView> _renderViews = [];
    private LogicalInputRecorder? _inputRecorder;
    private LogicalInputPlayback? _inputPlayback;
    private GameplayStateRecorder? _stateRecorder;
    private GameplayStateVerifier? _stateVerifier;
    private readonly HashSet<PointerId> _viewportDownPointers = [];
    private readonly Dictionary<PointerId, ViewportPointerCapture> _viewportPointerCaptures = [];
    private readonly List<PointerId> _viewportReleasedPointers = [];
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
        _tileMapRenderer = new TileMapRenderer(_tileSets);
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
        UpdateViewportNavigation(deltaTime);
        _scene.PerformStep(deltaTime);
        _audio?.Update();
        _sceneAudio.PruneCompleted();
        for (int i = 0; i < _renderViews.Count; i++)
            _renderViews[i].Camera.Update(deltaTime);
        _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        ApplyPendingSceneSwitch();
        _transforms.Synchronize();
        CaptureOrVerifyGameplayState();
        _contentHotReload?.Tick();
        _shaderHotReload?.Tick();
    }

    private void UpdateViewportNavigation(double deltaTime)
    {
        bool anyNavigation = false;
        for (int i = 0; i < _renderViews.Count; i++)
        {
            if (_renderViews[i].Navigation is not null)
            {
                anyNavigation = true;
                break;
            }
        }
        if (!anyNavigation) return;

        IInputProvider pointerInput = _window.Input;
        int pointerCount = pointerInput.PointerCount;
        if (pointerCount < 0 || pointerCount > MaximumViewportPointers)
            throw new InvalidOperationException(
                $"Viewport navigation supports between 0 and {MaximumViewportPointers} pointers.");
        Span<PointerContact> contacts = stackalloc PointerContact[pointerCount];
        Span<bool> wasPressed = stackalloc bool[pointerCount];
        for (int i = 0; i < pointerCount; i++)
        {
            PointerContact contact = pointerInput.GetPointer(i);
            for (int j = 0; j < i; j++)
            {
                if (contacts[j].Id == contact.Id)
                    throw new InvalidOperationException(
                        $"Input provider returned duplicate pointer '{contact.Id}'.");
            }
            contacts[i] = contact;
            if (!contact.IsDown) continue;
            wasPressed[i] = _viewportDownPointers.Add(contact.Id);
            if (!wasPressed[i] ||
                !Context.TryScreenToView(contact.Position, out ViewportHit pressedHit) ||
                Context.GetRenderView(pressedHit.View).Navigation is null)
            {
                continue;
            }
            _viewportPointerCaptures[contact.Id] = new ViewportPointerCapture(
                pressedHit.View,
                pressedHit.Slot);
        }

        _viewportReleasedPointers.Clear();
        foreach (PointerId down in _viewportDownPointers)
        {
            if (!ContainsDownPointer(contacts, down)) _viewportReleasedPointers.Add(down);
        }
        for (int i = 0; i < _viewportReleasedPointers.Count; i++)
            _viewportDownPointers.Remove(_viewportReleasedPointers[i]);

        var mouse = _window.Input.MousePosition;
        bool hasScrollTarget = Context.TryScreenToView(mouse, out ViewportHit scrollHit);
        float scroll = _window.Input.MouseScrollDelta;
        Span<GameEngine.Features.ViewportNavigation.ViewportPointer> routedPointers =
            stackalloc GameEngine.Features.ViewportNavigation.ViewportPointer[pointerCount];
        for (int i = 0; i < _renderViews.Count; i++)
        {
            RenderView view = _renderViews[i];
            if (view.Navigation is not { } navigation) continue;
            for (int pointerIndex = 0; pointerIndex < pointerCount; pointerIndex++)
            {
                PointerContact contact = contacts[pointerIndex];
                bool captured = _viewportPointerCaptures.TryGetValue(
                    contact.Id,
                    out ViewportPointerCapture capture) && capture.View == view.Ref;
                bool hasHit = Context.TryScreenToView(contact.Position, out ViewportHit hit);
                bool inside = hasHit && hit.View == view.Ref &&
                    (!_viewportPointerCaptures.TryGetValue(contact.Id, out ViewportPointerCapture owner) ||
                     owner.View == view.Ref);
                Vector2D position = inside ? hit.ViewPosition : default;
                if (captured)
                    Context.MapScreenToViewportPosition(contact.Position, capture.Slot, out position);
                routedPointers[pointerIndex] = new GameEngine.Features.ViewportNavigation.ViewportPointer(
                    contact.Id,
                    contact.Kind,
                    new Vector2((float)position.X, (float)position.Y),
                    inside,
                    captured,
                    contact.IsDown,
                    contact.IsPrimary,
                    wasPressed[pointerIndex]);
            }

            bool scrollInside = hasScrollTarget && scrollHit.View == view.Ref;
            Vector2D scrollPosition = scrollInside ? scrollHit.ViewPosition : default;
            var input = new GameEngine.Features.ViewportNavigation.ViewportInputFrame(
                routedPointers,
                new Vector2((float)scrollPosition.X, (float)scrollPosition.Y),
                scrollInside,
                scrollInside ? scroll : 0f);
            navigation.Update(in input, deltaTime);
        }

        for (int i = 0; i < pointerCount; i++)
        {
            if (contacts[i].IsDown) continue;
            _viewportDownPointers.Remove(contacts[i].Id);
            _viewportPointerCaptures.Remove(contacts[i].Id);
        }
        for (int i = 0; i < _viewportReleasedPointers.Count; i++)
            _viewportPointerCaptures.Remove(_viewportReleasedPointers[i]);
    }

    private static bool ContainsDownPointer(
        ReadOnlySpan<PointerContact> contacts,
        PointerId id)
    {
        for (int i = 0; i < contacts.Length; i++)
        {
            if (contacts[i].Id == id && contacts[i].IsDown) return true;
        }
        return false;
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
            view.ResizeCamera(renderWidth, renderHeight);
            view.Navigation?.Resize();
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
            _sceneAudio?.StopAll();
        }
        finally
        {
            try
            {
                Interlocked.Exchange(ref _sceneContent, null)?.Dispose();
            }
            finally
            {
                _resources.Dispose();
            }
        }
    }

    private readonly record struct ViewportPointerCapture(
        RenderViewRef View,
        ViewportSlotRef Slot);

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
        _audioClips = new AudioLibrary();
        if (_plan.Audio is { } audioOptions)
        {
            IAudioBackend backend;
            if (audioOptions.ForceSilentBackend)
            {
                backend = new SilentAudioBackend();
            }
            else
            {
                backend = OpenAlAudioBackend.CreateOrSilent(out string? failure, _audioClips);
                if (failure is not null &&
                    audioOptions.FailureMode == AudioInitializationFailureMode.Throw)
                {
                    backend.Dispose();
                    throw new InvalidOperationException("Audio device initialization failed.", new InvalidOperationException(failure));
                }
            }
            _audio = _resources.Add(new AudioRuntime(
                _audioClips,
                backend,
                audioOptions.MaxVoices,
                ownsBackend: true));
        }
        _sceneAudio = new SceneAudioScope(_audio);
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

        LoadedContentPackage? content = null;
        if (renderer.ContentPackagesRoot is { } configuredRoot)
        {
            string packagesRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(AppContext.BaseDirectory, configuredRoot));
            _contentManager = _resources.Add(new ContentPackageManager(
                _textures,
                _sprites,
                _animations,
                _audioClips,
                _tileSets,
                _tileMaps,
                packagesRoot));
            if (renderer.ContentCatalogOnly)
            {
                ContentPackageRef? initialPackage =
                    _plan.Scenes[_plan.InitialScene.Name].ContentPackage;
                _sceneContent = initialPackage is { } scenePackage
                    ? _contentManager.Load(scenePackage)
                    : null;
                content = _sceneContent;
            }
            else
            {
                _globalContent = _resources.Add(renderer.ContentPackage is { } package
                    ? _contentManager.Load(package)
                    : _contentManager.Load(renderer.ContentManifest!));
                content = _globalContent;
            }
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
                    renderer.MainNavigation,
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
            _audioClips,
            _audio,
            _sceneAudio,
            _tileSets,
            _tileMaps,
            _tileMapRenderer,
            _contentManager?.TileWorlds,
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
                _contentManager!,
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

        ISceneDefinition definition = _scenes.GetDefinition(next.Scene);
        LoadedContentPackage? nextContent = null;
        if (_plan.Renderer.ContentCatalogOnly && definition.ContentPackage is { } package)
            nextContent = _contentManager!.Load(package);

        bool contentAccepted = false;
        try
        {
            _scene!.TransitionTo(next.Scene.Name);
            _sceneAudio.StopAll();
            _builder.ApplyEvents(_scene.DrainUncommittedEvents());

            if (_plan.Renderer.ContentCatalogOnly)
            {
                LoadedContentPackage? previousContent = _sceneContent;
                _sceneContent = nextContent;
                Context.SetContent(nextContent);
                contentAccepted = true;
                previousContent?.Dispose();
            }

            _scenes.Commit(next.Scene);
            ConfigureScene(next);
            _scene.Start();
            _builder.ApplyEvents(_scene.DrainUncommittedEvents());
        }
        finally
        {
            if (!contentAccepted) nextContent?.Dispose();
        }
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
        ISceneDefinition definition = _scenes.GetDefinition(activation.Scene);
        ActivateSceneViews(definition);
        definition.Configure(Context, activation);
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

    private void ActivateSceneViews(ISceneDefinition scene)
    {
        ResetViewportInputState();
        for (int i = 0; i < _renderViews.Count; i++)
        {
            RenderView view = _renderViews[i];
            SceneRenderViewDefinition? configuration = null;
            if (scene.Views is not null &&
                scene.Views.TryGetValue(view.Ref.Name, out SceneRenderViewDefinition? configured))
            {
                configuration = configured;
            }
            view.ActivateScene(configuration);
        }
    }

    private void ResetViewportInputState()
    {
        _viewportDownPointers.Clear();
        _viewportPointerCaptures.Clear();
        _viewportReleasedPointers.Clear();

        IInputProvider input = _window.Input;
        int pointerCount = input.PointerCount;
        if (pointerCount < 0 || pointerCount > MaximumViewportPointers)
            throw new InvalidOperationException(
                $"Viewport navigation supports between 0 and {MaximumViewportPointers} pointers.");
        // A pointer held across the Scene boundary is not a fresh press in the new Scene.
        for (int i = 0; i < pointerCount; i++)
        {
            PointerContact contact = input.GetPointer(i);
            if (!_viewportDownPointers.Add(contact.Id))
                throw new InvalidOperationException(
                    $"Input provider returned duplicate pointer '{contact.Id}'.");
            if (!contact.IsDown) _viewportReleasedPointers.Add(contact.Id);
        }
        for (int i = 0; i < _viewportReleasedPointers.Count; i++)
            _viewportDownPointers.Remove(_viewportReleasedPointers[i]);
        _viewportReleasedPointers.Clear();
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
