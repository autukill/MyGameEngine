namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

public readonly record struct RenderEffectBuildContext(
    int Width,
    int Height,
    IRenderTargetPool Targets,
    IRenderSurfaceResolver Surfaces);

public readonly record struct RenderEffectCompositeSource(
    RenderTarget2D Source,
    ViewportRect Viewport,
    BlendState Blend);

/// <summary>Factory 创建的运行时附件；Pass 在挂接后由 Pipeline 释放，Runtime 释放租约。</summary>
public interface IRenderEffectRuntime : IDisposable
{
    RenderEffectKey Key { get; }
    IReadOnlyList<RenderPass> Passes { get; }
    IReadOnlyList<RenderEffectCompositeSource> CompositeSources { get; }
    IReadOnlyList<RenderEffectOutput> Outputs { get; }
    bool RequiresRebuild(IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) => false;
    void UpdateOwners(IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners);
}

public interface IRenderEffectFactory
{
    string Kind { get; }

    /// <summary>必须完成共享配置校验，并在分配 GPU 资源前声明逻辑依赖。</summary>
    RenderEffectPlan Plan(
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners);

    IRenderEffectRuntime Create(
        in RenderEffectBuildContext context,
        RenderEffectKey key,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners);
}

/// <summary>Builder 的可测试图编辑边界。</summary>
public interface IRenderEffectGraphEditor
{
    RenderPassHandle AddPass(RenderPass pass);
    bool RemovePass(RenderPassHandle handle);
    CompositeSourceHandle AddCompositeSource(in RenderEffectCompositeSource source);
    bool RemoveCompositeSource(CompositeSourceHandle handle);
}

internal sealed class RenderEffectGraphEditor : IRenderEffectGraphEditor
{
    private readonly RenderPipeline _pipeline;
    private readonly ViewportCompositorPass _compositor;

    public RenderEffectGraphEditor(RenderPipeline pipeline, ViewportCompositorPass compositor)
    {
        _pipeline = pipeline;
        _compositor = compositor;
    }

    public RenderPassHandle AddPass(RenderPass pass) => _pipeline.AddPass(pass);
    public bool RemovePass(RenderPassHandle handle) => _pipeline.RemovePass(handle);
    public CompositeSourceHandle AddCompositeSource(in RenderEffectCompositeSource source) =>
        _compositor.AddSource(source.Source, source.Viewport, source.Blend);
    public bool RemoveCompositeSource(CompositeSourceHandle handle) =>
        _compositor.RemoveSource(handle);
}
