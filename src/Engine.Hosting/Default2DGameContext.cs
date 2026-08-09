namespace GameEngine.Hosting;

using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Infrastructure;

/// <summary>Scene 装配期的强类型上下文；不是全局服务容器。</summary>
public sealed class Default2DGameContext
{
    private readonly Action _close;
    private readonly RenderTarget2D[] _rootRenderTargets;
    private readonly Dictionary<string, Func<long>> _customGpuMemory = new(StringComparer.Ordinal);
    public EngineWindow Window { get; }
    public SceneAggregate Scene { get; }
    public TextureLibrary Textures { get; }
    public SpriteLibrary Sprites { get; }
    public ShaderLibrary Shaders { get; }
    public LoadedContentPackage? Content { get; }
    public Camera2D Camera { get; }
    public RenderPipeline Pipeline { get; }
    public ScenePipelineBuilder Effects { get; }
    public RenderTargetPool RenderTargets { get; }

    internal Default2DGameContext(
        EngineWindow window,
        SceneAggregate scene,
        TextureLibrary textures,
        SpriteLibrary sprites,
        ShaderLibrary shaders,
        LoadedContentPackage? content,
        Camera2D camera,
        RenderPipeline pipeline,
        ScenePipelineBuilder effects,
        RenderTargetPool renderTargets,
        RenderTarget2D sceneTarget,
        RenderTarget2D? guiTarget,
        Action close)
    {
        Window = window;
        Scene = scene;
        Textures = textures;
        Sprites = sprites;
        Shaders = shaders;
        Content = content;
        Camera = camera;
        Pipeline = pipeline;
        Effects = effects;
        RenderTargets = renderTargets;
        _rootRenderTargets = guiTarget is null
            ? new[] { sceneTarget }
            : new[] { sceneTarget, guiTarget };
        _close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public SpriteRef GetSprite(string name) => RequireContent().GetSprite(name);

    public TextureRef GetTexture(string name) => RequireContent().GetTexture(name);

    public void RegisterRenderEffectFactory(IRenderEffectFactory factory) =>
        Effects.RegisterFactory(factory);

    public RenderPassHandle AddRenderPass(RenderPass pass) => Pipeline.AddPass(pass);

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
            frame);
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

    /// <summary>低频捕获帧计数、Texture/Atlas、根目标、Pool 与自定义资源估算。</summary>
    public RuntimePerformanceSnapshot CapturePerformanceSnapshot(
        PerformanceBudget? budget = null)
    {
        FrameStatisticsSnapshot? frame = TryCaptureFrameStatistics(out var frameSnapshot)
            ? frameSnapshot
            : null;
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
        IReadOnlyList<PerformanceBudgetViolation> violations = budget is null
            ? Array.Empty<PerformanceBudgetViolation>()
            : budget.Evaluate(frame, memory);
        return new RuntimePerformanceSnapshot(
            DateTimeOffset.UtcNow,
            frame,
            textures,
            memory,
            Array.AsReadOnly(custom),
            violations);
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

    /// <summary>请求在当前 Step/Draw 回调完成后的安全帧边界关闭窗口。</summary>
    public void Close() => _close();

    private LoadedContentPackage RequireContent() => Content ??
        throw new InvalidOperationException(
            "No content package is configured. Call UseContent on Default2DRendererOptions.");

    private sealed class GpuMemoryRegistration(
        Default2DGameContext owner,
        string name) : IDisposable
    {
        private Default2DGameContext? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?._customGpuMemory.Remove(name);
    }
}
