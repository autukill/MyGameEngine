namespace GameEngine.Hosting;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.TextRendering.Infrastructure;
using GameEngine.Features.TransformHierarchy.Gameplay;
using GameEngine.Features.Tilemaps.Infrastructure;
using GameEngine.Features.TileWorlds.Domain;
using GameEngine.Features.TileWorlds.Infrastructure;
using GameEngine.Features.TileWorldStreaming;
using GameEngine.Features.ViewportNavigation;

/// <summary>Scene 装配期的强类型上下文；不是全局服务容器。</summary>
public sealed class Default2DGameContext
{
    private readonly Action _close;
    private readonly RenderTarget2D[] _rootRenderTargets;
    private readonly ViewportBinding[] _viewportBindings;
    private readonly Dictionary<string, Func<long>> _customGpuMemory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CpuMemoryContributor> _customCpuMemory = new(StringComparer.Ordinal);
    public EngineWindow Window { get; }
    public SceneAggregate Scene { get; }
    public TextureLibrary Textures { get; }
    public SpriteLibrary Sprites { get; }
    public AnimationLibrary Animations { get; }
    public AudioLibrary AudioClips { get; }
    public TileSetLibrary TileSets { get; }
    public TileMapLibrary TileMaps { get; }
    public TileMapRenderer TileMapRenderer { get; }
    public TileWorldLibrary? TileWorlds { get; }
    public bool AudioEnabled => _audio is not null;
    public AudioRuntime Audio => _audio ?? throw new InvalidOperationException(
        "Audio is not enabled. Call GameApplicationBuilder.UseAudio before Build.");
    /// <summary>Playback automatically stopped when the active Scene ends.</summary>
    public SceneAudioScope SceneAudio { get; }
    /// <summary>Scene-scoped parent/child transforms and lightweight gameplay attachments.</summary>
    public SceneTransformRuntime Transforms { get; }
    public TextRuntime Text { get; }
    public ShaderLibrary Shaders { get; }
    /// <summary>The global package, or the lease owned by the currently active Scene.</summary>
    public LoadedContentPackage? Content { get; private set; }
    /// <summary>The persistent main View Camera reset to the active Scene's declared state.</summary>
    public Camera2D Camera { get; }
    public RenderPipeline Pipeline { get; }
    public ScenePipelineBuilder Effects { get; }
    public RenderTargetPool RenderTargets { get; }
    public SceneNavigator Scenes { get; }
    public IInstanceFactory Instances { get; }
    public InputMap InputMap { get; }
    public GameplayTimeController Time => Scene.Time;
    public IReadOnlyList<SingleCameraViewportDefinition> Viewports { get; }
    public IReadOnlyList<RenderView> RenderViews { get; }
    private readonly AudioRuntime? _audio;

    internal Default2DGameContext(
        EngineWindow window,
        SceneAggregate scene,
        TextureLibrary textures,
        SpriteLibrary sprites,
        AnimationLibrary animations,
        AudioLibrary audioClips,
        AudioRuntime? audio,
        SceneAudioScope sceneAudio,
        TileSetLibrary tileSets,
        TileMapLibrary tileMaps,
        TileMapRenderer tilemapRenderer,
        TileWorldLibrary? tileWorlds,
        SceneTransformRuntime transforms,
        TextRuntime text,
        ShaderLibrary shaders,
        LoadedContentPackage? content,
        Camera2D camera,
        RenderPipeline pipeline,
        ScenePipelineBuilder effects,
        RenderTargetPool renderTargets,
        RenderTarget2D sceneTarget,
        RenderTarget2D? guiTarget,
        IReadOnlyList<SingleCameraViewportDefinition> viewports,
        IReadOnlyList<RenderView> renderViews,
        SceneNavigator scenes,
        IInstanceFactory instances,
        InputMap inputMap,
        Action close)
    {
        Window = window;
        Scene = scene;
        Textures = textures;
        Sprites = sprites;
        Animations = animations ?? throw new ArgumentNullException(nameof(animations));
        AudioClips = audioClips ?? throw new ArgumentNullException(nameof(audioClips));
        TileSets = tileSets ?? throw new ArgumentNullException(nameof(tileSets));
        TileMaps = tileMaps ?? throw new ArgumentNullException(nameof(tileMaps));
        TileMapRenderer = tilemapRenderer ?? throw new ArgumentNullException(nameof(tilemapRenderer));
        TileWorlds = tileWorlds;
        _audio = audio;
        SceneAudio = sceneAudio ?? throw new ArgumentNullException(nameof(sceneAudio));
        Transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Shaders = shaders;
        Content = content;
        Camera = camera;
        Pipeline = pipeline;
        Effects = effects;
        RenderTargets = renderTargets;
        ArgumentNullException.ThrowIfNull(sceneTarget);
        ArgumentNullException.ThrowIfNull(viewports);
        ArgumentNullException.ThrowIfNull(renderViews);
        if (renderViews.Count == 0)
            throw new ArgumentException("At least one Render View is required.", nameof(renderViews));
        RenderViews = renderViews;
        if (renderViews.Count == 1)
        {
            Viewports = viewports;
            _viewportBindings = new ViewportBinding[viewports.Count];
            for (int i = 0; i < viewports.Count; i++)
                _viewportBindings[i] = new ViewportBinding(viewports[i], renderViews[0]);
        }
        else
        {
            var resolvedViewports = new SingleCameraViewportDefinition[renderViews.Count];
            _viewportBindings = new ViewportBinding[renderViews.Count];
            for (int i = 0; i < renderViews.Count; i++)
            {
                RenderView view = renderViews[i];
                var viewport = new SingleCameraViewportDefinition(
                    view.Slot,
                    view.Viewport,
                    view.Fit,
                    view.Layer,
                    view.DeclarationOrder);
                resolvedViewports[i] = viewport;
                _viewportBindings[i] = new ViewportBinding(viewport, view);
            }
            Viewports = Array.AsReadOnly(resolvedViewports);
        }
        Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
        Instances = instances ?? throw new ArgumentNullException(nameof(instances));
        InputMap = inputMap ?? throw new ArgumentNullException(nameof(inputMap));
        _rootRenderTargets = new RenderTarget2D[renderViews.Count + (guiTarget is null ? 0 : 1)];
        for (int i = 0; i < renderViews.Count; i++)
            _rootRenderTargets[i] = renderViews[i].Target;
        if (guiTarget is not null) _rootRenderTargets[^1] = guiTarget;
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public SpriteRef GetSprite(string name) => RequireContent().GetSprite(name);

    public AnimationClipRef GetAnimation(string name) => RequireContent().GetAnimation(name);

    public AudioClipRef GetAudioClip(string name) => RequireContent().GetAudioClip(name);

    public TextureRef GetTexture(string name) => RequireContent().GetTexture(name);

    public MaterialRef GetMaterial(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Shaders.TryGetMaterial(name)?.Ref ??
            throw new KeyNotFoundException($"Material '{name}' is not registered.");
    }

    public void RegisterRenderEffectFactory(IRenderEffectFactory factory) =>
        Effects.RegisterFactory(factory);

    public RenderPassHandle AddRenderPass(RenderPass pass) => Pipeline.AddPass(pass);

    /// <summary>
    /// Presents a world-space Display Surface through every configured Viewport.
    /// Call during Scene configuration for custom Stencil or LDR overlay outputs.
    /// </summary>
    public void PresentWorldSurface(
        RenderSurfaceKey source,
        int layer = 0,
        PresentationBlendMode blend = PresentationBlendMode.AlphaBlend)
    {
        if (!source.IsValid)
            throw new ArgumentException("Presentation source must be initialized.", nameof(source));
        if (!Enum.IsDefined(blend)) throw new ArgumentOutOfRangeException(nameof(blend));
        for (int i = 0; i < Viewports.Count; i++)
        {
            SingleCameraViewportDefinition viewport = Viewports[i];
            Scene.Add(new DefaultWorldPresentationController(
                Scene.RaiseEvent,
                source,
                viewport,
                checked(layer + viewport.Layer),
                blend));
        }
    }

    /// <summary>Presents a Surface only through slots backed by the selected Render View.</summary>
    public void PresentViewSurface(
        RenderViewRef view,
        RenderSurfaceKey source,
        int layer = 0,
        PresentationBlendMode blend = PresentationBlendMode.AlphaBlend)
    {
        if (view.IsEmpty) throw new ArgumentException("Render View cannot be empty.", nameof(view));
        if (!source.IsValid)
            throw new ArgumentException("Presentation source must be initialized.", nameof(source));
        if (!Enum.IsDefined(blend)) throw new ArgumentOutOfRangeException(nameof(blend));
        bool found = false;
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding binding = _viewportBindings[i];
            if (binding.View.Ref != view) continue;
            found = true;
            Scene.Add(new DefaultWorldPresentationController(
                Scene.RaiseEvent,
                source,
                binding.Viewport,
                checked(layer + binding.Viewport.Layer),
                blend));
        }
        if (!found) throw new KeyNotFoundException($"Render View '{view}' is not configured.");
    }

    public RenderView GetRenderView(RenderViewRef view)
    {
        for (int i = 0; i < RenderViews.Count; i++)
        {
            if (RenderViews[i].Ref == view) return RenderViews[i];
        }
        throw new KeyNotFoundException($"Render View '{view}' is not configured.");
    }

    /// <summary>Gets the follow controller owned by the active Scene for a Render View.</summary>
    public CameraFollowController GetCameraFollow(RenderViewRef view) =>
        GetRenderView(view).RequireCameraFollow();

    /// <summary>Gets the interactive Viewport controller owned by the active Scene.</summary>
    public ViewportController GetViewportNavigation(RenderViewRef view) =>
        GetRenderView(view).RequireNavigation();

    /// <summary>
    /// Creates a Scene-owned TileWorld streaming session. Dispose the session before its Content
    /// package lease is released so Chunk textures are removed before the archive is unregistered.
    /// </summary>
    public TileWorldStreamingSession CreateTileWorldStream(
        TileWorldRef world,
        TileWorldStreamingOptions? options = null,
        IImageDecoder? decoder = null)
    {
        if (TileWorlds is null)
            throw new InvalidOperationException(
                "TileWorld content is unavailable. Configure a Content package before creating a stream.");
        return new TileWorldStreamingSession(
            TileWorlds.Get(world),
            TileSets,
            Textures,
            decoder,
            options);
    }

    /// <summary>显式捕获当前 Pass、Surface、Effect owner 与临时目标租约。</summary>
    public Default2DRenderDiagnostics CaptureRenderDiagnostics()
    {
        FrameStatisticsSnapshot? frame = TryCaptureFrameStatistics(out var snapshot)
            ? snapshot
            : null;
        return new Default2DRenderDiagnostics(
            Pipeline.CaptureDiagnostics(),
            Effects.CaptureDiagnostics(),
            RenderTargets.CaptureDiagnostics(),
            frame,
            CaptureViewportDiagnostics());
    }

    /// <summary>
    /// Resolves the topmost Viewport under a screen point and maps it through the
    /// stable Camera transform. Contain letterbox regions do not count as a hit.
    /// </summary>
    public bool TryScreenToView(Vector2D screenPosition, out ViewportHit hit)
    {
        ViewportBinding? best = null;
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding candidate = _viewportBindings[i];
            ViewportPlacement placement = ResolvePlacement(candidate);
            if (!placement.Contains((float)screenPosition.X, (float)screenPosition.Y)) continue;
            if (best is null || candidate.Viewport.Layer > best.Viewport.Layer ||
                candidate.Viewport.Layer == best.Viewport.Layer &&
                candidate.Viewport.DeclarationOrder > best.Viewport.DeclarationOrder)
            {
                best = candidate;
            }
        }
        if (best is not null) return TryScreenToView(screenPosition, best, out hit);
        hit = default;
        return false;
    }

    public bool TryScreenToView(
        Vector2D screenPosition,
        ViewportSlotRef slot,
        out ViewportHit hit)
    {
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding binding = _viewportBindings[i];
            if (binding.Viewport.Slot == slot)
                return TryScreenToView(screenPosition, binding, out hit);
        }
        hit = default;
        return false;
    }

    public bool TryScreenToView(
        Vector2D screenPosition,
        RenderViewRef view,
        out ViewportHit hit)
    {
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding binding = _viewportBindings[i];
            if (binding.View.Ref == view)
                return TryScreenToView(screenPosition, binding, out hit);
        }
        hit = default;
        return false;
    }

    public bool TryScreenToWorld(
        Vector2D screenPosition,
        out Vector2D worldPosition,
        out ViewportSlotRef slot)
    {
        if (TryScreenToView(screenPosition, out ViewportHit hit))
        {
            worldPosition = hit.WorldPosition;
            slot = hit.Slot;
            return true;
        }
        worldPosition = default;
        slot = default;
        return false;
    }

    public IReadOnlyList<ViewportSlotDiagnostics> CaptureViewportDiagnostics()
    {
        var result = new ViewportSlotDiagnostics[_viewportBindings.Length];
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding binding = _viewportBindings[i];
            SingleCameraViewportDefinition viewport = binding.Viewport;
            ViewportPlacement placement = ResolvePlacement(binding);
            result[i] = new ViewportSlotDiagnostics(
                binding.View.Ref,
                viewport.Slot,
                viewport.Viewport,
                viewport.Fit,
                viewport.Layer,
                placement.X,
                placement.Y,
                placement.Width,
                placement.Height,
                binding.View.Target.Width,
                binding.View.Target.Height,
                binding.View.SceneLayers,
                binding.View.Effects,
                binding.View.LastSceneDraw);
        }
        return Array.AsReadOnly(result);
    }

    /// <summary>运行时更新 VSync、渲染 FPS 与更新 UPS 目标；0 表示不限速。</summary>
    public void SetFrameRate(FrameRateSettings settings) => Window.SetFrameRate(settings);

    public bool TryCaptureFrameStatistics(out FrameStatisticsSnapshot snapshot)
    {
        if (Window.FrameStatistics is { } statistics)
            return statistics.TryCapture(out snapshot);
        snapshot = default;
        return false;
    }

    public void SetGameplayQueryStatisticsEnabled(bool enabled) =>
        Scene.SetGameplayQueryStatisticsEnabled(enabled);

    public GameplayQueryStatisticsSnapshot CaptureGameplayQueryStatistics(bool reset = false) =>
        Scene.CaptureGameplayQueryStatistics(reset);

    /// <summary>
    /// Low-frequency snapshot of process working/private memory and the managed GC heap.
    /// It is independent from frame statistics and only forces a full collection when explicitly
    /// requested for a developer diagnostic checkpoint.
    /// </summary>
    public ProcessMemoryDiagnostics CaptureProcessMemoryDiagnostics(
        bool forceFullCollection = false) =>
        ProcessMemoryDiagnostics.CaptureCurrentProcess(forceFullCollection);

    /// <summary>低频捕获帧计数、Texture/Atlas、根目标、Pool 与自定义资源估算。</summary>
    public RuntimePerformanceSnapshot CapturePerformanceSnapshot(
        PerformanceBudget? budget = null,
        bool resetGameplayQueryStatistics = false)
    {
        FrameStatisticsSnapshot? frame = TryCaptureFrameStatistics(out var frameSnapshot)
            ? frameSnapshot
            : null;
        GameplayQueryStatisticsSnapshot gameplayQueries =
            Scene.CaptureGameplayQueryStatistics(resetGameplayQueryStatistics);
        TextureLibraryDiagnostics textures = Textures.CaptureDiagnostics();
        RenderTargetPoolDiagnostics pool = RenderTargets.CaptureDiagnostics();
        long rootBytes = 0;
        foreach (RenderTarget2D target in _rootRenderTargets)
            rootBytes = checked(rootBytes + RenderTargetMemoryEstimator.EstimateBytes(target.Descriptor));

        long leasedBytes = 0;
        long availableBytes = 0;
        foreach (RenderTargetDescriptorDiagnostics item in pool.Descriptors)
        {
            long bytes = RenderTargetMemoryEstimator.EstimateBytes(item.Descriptor);
            leasedBytes = checked(leasedBytes + bytes * item.LeasedCount);
            availableBytes = checked(availableBytes + bytes * item.AvailableCount);
        }

        var custom = _customGpuMemory
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                long bytes = pair.Value();
                if (bytes < 0)
                    throw new InvalidOperationException(
                        $"GPU memory contributor '{pair.Key}' returned a negative estimate.");
                return new CustomGpuMemoryDiagnostics(pair.Key, bytes);
            })
            .ToArray();
        long customBytes = custom.Sum(item => item.EstimatedBytes);
        var memory = new GpuMemoryEstimate(
            textures.TextureCount,
            textures.EstimatedBytes,
            _rootRenderTargets.Length,
            rootBytes,
            pool.LeasedCount,
            leasedBytes,
            pool.AvailableCount,
            availableBytes,
            custom.Length,
            customBytes);
        var customCpu = _customCpuMemory
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                long bytes = pair.Value.EstimateBytes();
                if (bytes < 0)
                    throw new InvalidOperationException(
                        $"CPU memory contributor '{pair.Key}' returned a negative estimate.");
                return new CustomCpuMemoryDiagnostics(pair.Key, pair.Value.Domain, bytes);
            })
            .ToArray();
        var cpuAttribution = new CpuMemoryAttributionEstimate(
            customCpu.Count(item => item.Domain == CpuMemoryDomain.Managed),
            customCpu.Where(item => item.Domain == CpuMemoryDomain.Managed)
                .Sum(item => item.EstimatedBytes),
            customCpu.Count(item => item.Domain == CpuMemoryDomain.Native),
            customCpu.Where(item => item.Domain == CpuMemoryDomain.Native)
                .Sum(item => item.EstimatedBytes));
        IReadOnlyList<PerformanceBudgetViolation> violations = budget is null
            ? Array.Empty<PerformanceBudgetViolation>()
            : budget.Evaluate(frame, memory);
        ProcessMemoryDiagnostics processMemory = CaptureProcessMemoryDiagnostics();
        return new RuntimePerformanceSnapshot(
            DateTimeOffset.UtcNow,
            frame,
            gameplayQueries,
            textures,
            memory,
            Array.AsReadOnly(custom),
            cpuAttribution,
            Array.AsReadOnly(customCpu),
            violations,
            processMemory);
    }

    /// <summary>为绕过 TextureLibrary/RenderTargetPool 的高级 GPU 资源补充估算。</summary>
    public IDisposable RegisterGpuMemoryUsage(string name, Func<long> estimateBytes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("GPU memory usage name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(estimateBytes);
        if (!_customGpuMemory.TryAdd(name, estimateBytes))
            throw new ArgumentException(
                $"GPU memory usage '{name}' is already registered.", nameof(name));
        return new GpuMemoryRegistration(this, name);
    }

    /// <summary>
    /// Registers a low-frequency ownership estimate for managed or native CPU memory. The value is
    /// attribution only and is never added to Working Set or Private Bytes.
    /// </summary>
    public IDisposable RegisterCpuMemoryUsage(
        string name,
        CpuMemoryDomain domain,
        Func<long> estimateBytes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("CPU memory usage name cannot be empty.", nameof(name));
        if (!Enum.IsDefined(domain)) throw new ArgumentOutOfRangeException(nameof(domain));
        ArgumentNullException.ThrowIfNull(estimateBytes);
        if (!_customCpuMemory.TryAdd(name, new CpuMemoryContributor(domain, estimateBytes)))
            throw new ArgumentException(
                $"CPU memory usage '{name}' is already registered.", nameof(name));
        return new CpuMemoryRegistration(this, name);
    }

    /// <summary>请求在当前 Step/Draw 回调完成后的安全帧边界关闭窗口。</summary>
    public void Close() => _close();

    internal void SetContent(LoadedContentPackage? content) => Content = content;

    private LoadedContentPackage RequireContent() => Content ??
        throw new InvalidOperationException(
            "No content package is configured. Call UseContent on Default2DRendererOptions.");

    private bool TryScreenToView(
        Vector2D screenPosition,
        ViewportBinding binding,
        out ViewportHit hit)
    {
        ViewportPlacement placement = ResolvePlacement(binding);
        if (!placement.Contains((float)screenPosition.X, (float)screenPosition.Y))
        {
            hit = default;
            return false;
        }
        Vector2 view = placement.ScreenToSource(
            (float)screenPosition.X,
            (float)screenPosition.Y,
            binding.View.Target.Width,
            binding.View.Target.Height);
        if (!binding.View.Camera.TryViewportToWorld(view, out Vector2 world))
        {
            hit = default;
            return false;
        }
        hit = new ViewportHit(
            binding.View.Ref,
            binding.Viewport.Slot,
            screenPosition,
            new Vector2D(view.X, view.Y),
            new Vector2D(world.X, world.Y));
        return true;
    }

    private ViewportPlacement ResolvePlacement(ViewportBinding binding) =>
        ViewportPlacement.Calculate(
            binding.View.Target.Width,
            binding.View.Target.Height,
            Window.Width,
            Window.Height,
            binding.Viewport.Viewport,
            binding.Viewport.Fit);

    internal void MapScreenToViewportPosition(
        Vector2D screenPosition,
        ViewportSlotRef slot,
        out Vector2D viewPosition)
    {
        for (int i = 0; i < _viewportBindings.Length; i++)
        {
            ViewportBinding binding = _viewportBindings[i];
            if (binding.Viewport.Slot != slot) continue;
            ViewportPlacement placement = ResolvePlacement(binding);
            Vector2 source = placement.ScreenToSourceClamped(
                (float)screenPosition.X,
                (float)screenPosition.Y,
                binding.View.Target.Width,
                binding.View.Target.Height);
            viewPosition = new Vector2D(source.X, source.Y);
            return;
        }
        throw new KeyNotFoundException($"Viewport slot '{slot}' is not configured.");
    }

    private sealed record ViewportBinding(
        SingleCameraViewportDefinition Viewport,
        RenderView View);

    private readonly record struct CpuMemoryContributor(
        CpuMemoryDomain Domain,
        Func<long> EstimateBytes);

    private sealed class GpuMemoryRegistration(
        Default2DGameContext owner,
        string name) : IDisposable
    {
        private Default2DGameContext? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?._customGpuMemory.Remove(name);
    }

    private sealed class CpuMemoryRegistration(
        Default2DGameContext owner,
        string name) : IDisposable
    {
        private Default2DGameContext? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?._customCpuMemory.Remove(name);
    }
}
