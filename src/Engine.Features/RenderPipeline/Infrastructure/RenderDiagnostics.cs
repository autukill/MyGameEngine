namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

public sealed record RenderPassDiagnostics(
    RenderPassHandle Handle,
    int AttachmentIndex,
    int? ExecutionIndex,
    string Name,
    bool IsEnabled,
    bool WritesToScreen,
    int InputCount,
    RenderTargetDescriptor? Output);

public sealed class RenderPipelineDiagnostics
{
    public IReadOnlyList<RenderPassDiagnostics> Passes { get; }
    public string? DependencyError { get; }

    internal RenderPipelineDiagnostics(
        IEnumerable<RenderPassDiagnostics> passes,
        string? dependencyError)
    {
        Passes = Array.AsReadOnly(passes.ToArray());
        DependencyError = dependencyError;
    }
}

public sealed class RenderEffectDiagnostics
{
    public int Order { get; }
    public RenderEffectKey Key { get; }
    public IReadOnlyList<InstanceId> Owners { get; }
    public IReadOnlyList<RenderSurfaceSpec> Inputs { get; }
    public IReadOnlyList<RenderSurfaceSpec> Outputs { get; }
    public IReadOnlyList<RenderPassHandle> Passes { get; }

    internal RenderEffectDiagnostics(
        int order,
        RenderEffectKey key,
        IEnumerable<InstanceId> owners,
        IEnumerable<RenderSurfaceSpec> inputs,
        IEnumerable<RenderSurfaceSpec> outputs,
        IEnumerable<RenderPassHandle> passes)
    {
        Order = order;
        Key = key;
        Owners = Array.AsReadOnly(owners.ToArray());
        Inputs = Array.AsReadOnly(inputs.ToArray());
        Outputs = Array.AsReadOnly(outputs.ToArray());
        Passes = Array.AsReadOnly(passes.ToArray());
    }
}

public sealed class RenderSurfaceDiagnostics
{
    public RenderSurfaceSpec Spec { get; }
    public bool IsRoot { get; }
    public RenderEffectKey? Producer { get; }
    public IReadOnlyList<RenderEffectKey> Consumers { get; }

    internal RenderSurfaceDiagnostics(
        RenderSurfaceSpec spec,
        bool isRoot,
        RenderEffectKey? producer,
        IEnumerable<RenderEffectKey> consumers)
    {
        Spec = spec;
        IsRoot = isRoot;
        Producer = producer;
        Consumers = Array.AsReadOnly(consumers.ToArray());
    }
}

public sealed class ScenePipelineDiagnostics
{
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<RenderEffectDiagnostics> Effects { get; }
    public IReadOnlyList<RenderSurfaceDiagnostics> Surfaces { get; }

    internal ScenePipelineDiagnostics(
        int width,
        int height,
        IEnumerable<RenderEffectDiagnostics> effects,
        IEnumerable<RenderSurfaceDiagnostics> surfaces)
    {
        Width = width;
        Height = height;
        Effects = Array.AsReadOnly(effects.ToArray());
        Surfaces = Array.AsReadOnly(surfaces.ToArray());
    }
}

public sealed record RenderTargetDescriptorDiagnostics(
    RenderTargetDescriptor Descriptor,
    int TotalCount,
    int LeasedCount,
    int AvailableCount);

public sealed record RenderTargetLeaseDiagnostics(
    long LeaseId,
    RenderTargetDescriptor Descriptor);

public sealed class RenderTargetPoolDiagnostics
{
    public int TotalCount { get; }
    public int LeasedCount { get; }
    public int AvailableCount { get; }
    public IReadOnlyList<RenderTargetDescriptorDiagnostics> Descriptors { get; }
    public IReadOnlyList<RenderTargetLeaseDiagnostics> ActiveLeases { get; }

    internal RenderTargetPoolDiagnostics(
        int totalCount,
        int leasedCount,
        int availableCount,
        IEnumerable<RenderTargetDescriptorDiagnostics> descriptors,
        IEnumerable<RenderTargetLeaseDiagnostics> activeLeases)
    {
        TotalCount = totalCount;
        LeasedCount = leasedCount;
        AvailableCount = availableCount;
        Descriptors = Array.AsReadOnly(descriptors.ToArray());
        ActiveLeases = Array.AsReadOnly(activeLeases.ToArray());
    }
}
