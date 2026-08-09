namespace GameEngine.Features.RenderPipeline.Domain;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>逻辑渲染效果标识。Kind 选择工厂，Slot 区分同类效果的共享实例。</summary>
public readonly record struct RenderEffectKey
{
    public string Kind { get; }
    public string Slot { get; }
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Kind) && !string.IsNullOrWhiteSpace(Slot);

    public RenderEffectKey(string kind, string slot)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("Effect kind cannot be empty.", nameof(kind));
        if (string.IsNullOrWhiteSpace(slot))
            throw new ArgumentException("Effect slot cannot be empty.", nameof(slot));
        Kind = kind;
        Slot = slot;
    }

    public override string ToString() => $"{Kind}:{Slot}";
}

/// <summary>不包含 GPU 对象的逻辑渲染表面标识。</summary>
public readonly record struct RenderSurfaceKey
{
    public string ProducerKind { get; }
    public string ProducerSlot { get; }
    public string Output { get; }
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ProducerKind) &&
        !string.IsNullOrWhiteSpace(ProducerSlot) &&
        !string.IsNullOrWhiteSpace(Output);

    public static RenderSurfaceKey SceneColor => new("scene", "main", "color");
    public static RenderSurfaceKey SceneGui => new("scene", "main", "gui");

    public RenderSurfaceKey(string producerKind, string producerSlot, string output)
    {
        if (string.IsNullOrWhiteSpace(producerKind))
            throw new ArgumentException("Surface producer kind cannot be empty.", nameof(producerKind));
        if (string.IsNullOrWhiteSpace(producerSlot))
            throw new ArgumentException("Surface producer slot cannot be empty.", nameof(producerSlot));
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Surface output cannot be empty.", nameof(output));
        ProducerKind = producerKind;
        ProducerSlot = producerSlot;
        Output = output;
    }

    public static RenderSurfaceKey FromEffect(RenderEffectKey key, string output) =>
        new(key.Kind, key.Slot, output);

    public override string ToString() => $"{ProducerKind}:{ProducerSlot}.{Output}";
}

public enum RenderSurfaceEncoding
{
    Linear,
    Display
}

/// <summary>逻辑表面的物理存储格式与颜色编码契约。</summary>
public readonly record struct RenderSurfaceSpec
{
    public RenderSurfaceKey Key { get; }
    public RenderTargetColorFormat ColorFormat { get; }
    public RenderSurfaceEncoding Encoding { get; }

    public RenderSurfaceSpec(
        RenderSurfaceKey key,
        RenderTargetColorFormat colorFormat,
        RenderSurfaceEncoding encoding)
    {
        if (!key.IsValid)
            throw new ArgumentException("Surface key must be initialized.", nameof(key));
        if (!Enum.IsDefined(colorFormat))
            throw new ArgumentOutOfRangeException(nameof(colorFormat));
        if (!Enum.IsDefined(encoding))
            throw new ArgumentOutOfRangeException(nameof(encoding));
        Key = key;
        ColorFormat = colorFormat;
        Encoding = encoding;
    }

    public static RenderSurfaceSpec Ldr(RenderSurfaceKey key) =>
        new(key, RenderTargetColorFormat.Rgba8, RenderSurfaceEncoding.Display);

    public static RenderSurfaceSpec Hdr(RenderSurfaceKey key) =>
        new(key, RenderTargetColorFormat.Rgba16Float, RenderSurfaceEncoding.Linear);
}

/// <summary>工厂在分配 GPU 资源前声明的纯逻辑输入/输出计划。</summary>
public sealed class RenderEffectPlan : IEquatable<RenderEffectPlan>
{
    private readonly RenderSurfaceSpec[] _inputSurfaces;
    private readonly RenderSurfaceSpec[] _outputSurfaces;
    private readonly RenderSurfaceKey[] _inputs;
    private readonly RenderSurfaceKey[] _outputs;
    private readonly IReadOnlyList<RenderSurfaceKey> _inputView;
    private readonly IReadOnlyList<RenderSurfaceKey> _outputView;

    public RenderEffectKey Key { get; }
    public IReadOnlyList<RenderSurfaceKey> Inputs => _inputView;
    public IReadOnlyList<RenderSurfaceKey> Outputs => _outputView;
    public IReadOnlyList<RenderSurfaceSpec> InputSurfaces { get; }
    public IReadOnlyList<RenderSurfaceSpec> OutputSurfaces { get; }

    public RenderEffectPlan(
        RenderEffectKey key,
        IEnumerable<RenderSurfaceKey>? inputs = null,
        IEnumerable<RenderSurfaceKey>? outputs = null)
        : this(
            key,
            inputs?.Select(RenderSurfaceSpec.Ldr),
            outputs?.Select(RenderSurfaceSpec.Ldr))
    {
    }

    public RenderEffectPlan(
        RenderEffectKey key,
        IEnumerable<RenderSurfaceSpec>? inputSurfaces,
        IEnumerable<RenderSurfaceSpec>? outputSurfaces)
    {
        if (!key.IsValid)
            throw new ArgumentException("Effect key must be initialized.", nameof(key));
        Key = key;
        _inputSurfaces = inputSurfaces?.ToArray() ?? Array.Empty<RenderSurfaceSpec>();
        _outputSurfaces = outputSurfaces?.ToArray() ?? Array.Empty<RenderSurfaceSpec>();
        ValidateSurfaces(_inputSurfaces, nameof(inputSurfaces));
        ValidateSurfaces(_outputSurfaces, nameof(outputSurfaces));
        _inputs = _inputSurfaces.Select(surface => surface.Key).ToArray();
        _outputs = _outputSurfaces.Select(surface => surface.Key).ToArray();
        _inputView = Array.AsReadOnly(_inputs);
        _outputView = Array.AsReadOnly(_outputs);
        InputSurfaces = Array.AsReadOnly(_inputSurfaces);
        OutputSurfaces = Array.AsReadOnly(_outputSurfaces);
    }

    public bool Equals(RenderEffectPlan? other) =>
        other is not null &&
        Key == other.Key &&
        _inputSurfaces.SequenceEqual(other._inputSurfaces) &&
        _outputSurfaces.SequenceEqual(other._outputSurfaces);

    public override bool Equals(object? obj) => Equals(obj as RenderEffectPlan);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        foreach (var input in _inputSurfaces) hash.Add(input);
        foreach (var output in _outputSurfaces) hash.Add(output);
        return hash.ToHashCode();
    }

    private static void ValidateSurfaces(RenderSurfaceSpec[] surfaces, string parameterName)
    {
        if (surfaces.Any(surface => !surface.Key.IsValid))
            throw new ArgumentException("Surface specifications must be initialized.", parameterName);
        if (surfaces.Select(surface => surface.Key).Distinct().Count() != surfaces.Length)
            throw new ArgumentException("Surface keys cannot be duplicated within a plan.", parameterName);
    }
}

/// <summary>领域层效果描述符；实现不得携带 GL、Shader、Pass 或绘制回调。</summary>
public interface IRenderEffectDescriptor
{
    RenderEffectKey Key { get; }
}

public sealed record RenderEffectRequestedEvent(
    InstanceId OwnerId,
    IRenderEffectDescriptor Descriptor) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public sealed record RenderEffectReleasedEvent(
    InstanceId OwnerId,
    RenderEffectKey EffectKey) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
