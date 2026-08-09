namespace GameEngine.Hosting;

using System.Diagnostics;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Features.TextureAssets.Infrastructure;

public enum PerformanceMetric
{
    DrawCalls,
    BatchFlushes,
    TextureSwitches,
    ActivePasses,
    EstimatedGpuMemoryBytes
}

/// <summary>可选开发期性能上限；null 表示不评估该指标。</summary>
public sealed record PerformanceBudget
{
    public int? MaxDrawCalls { get; }
    public int? MaxBatchFlushes { get; }
    public int? MaxTextureSwitches { get; }
    public int? MaxActivePasses { get; }
    public long? MaxEstimatedGpuMemoryBytes { get; }

    public PerformanceBudget(
        int? maxDrawCalls = null,
        int? maxBatchFlushes = null,
        int? maxTextureSwitches = null,
        int? maxActivePasses = null,
        long? maxEstimatedGpuMemoryBytes = null)
    {
        ValidateLimit(maxDrawCalls, nameof(maxDrawCalls));
        ValidateLimit(maxBatchFlushes, nameof(maxBatchFlushes));
        ValidateLimit(maxTextureSwitches, nameof(maxTextureSwitches));
        ValidateLimit(maxActivePasses, nameof(maxActivePasses));
        ValidateLimit(maxEstimatedGpuMemoryBytes, nameof(maxEstimatedGpuMemoryBytes));
        MaxDrawCalls = maxDrawCalls;
        MaxBatchFlushes = maxBatchFlushes;
        MaxTextureSwitches = maxTextureSwitches;
        MaxActivePasses = maxActivePasses;
        MaxEstimatedGpuMemoryBytes = maxEstimatedGpuMemoryBytes;
    }

    public IReadOnlyList<PerformanceBudgetViolation> Evaluate(
        FrameStatisticsSnapshot? frame,
        GpuMemoryEstimate gpuMemory)
    {
        var violations = new List<PerformanceBudgetViolation>(5);
        if (frame is { } value)
        {
            AddIfExceeded(violations, PerformanceMetric.DrawCalls,
                value.DrawCalls, MaxDrawCalls);
            AddIfExceeded(violations, PerformanceMetric.BatchFlushes,
                value.BatchFlushes, MaxBatchFlushes);
            AddIfExceeded(violations, PerformanceMetric.TextureSwitches,
                value.TextureSwitches, MaxTextureSwitches);
            AddIfExceeded(violations, PerformanceMetric.ActivePasses,
                value.ActivePasses, MaxActivePasses);
        }
        AddIfExceeded(violations, PerformanceMetric.EstimatedGpuMemoryBytes,
            gpuMemory.TotalBytes, MaxEstimatedGpuMemoryBytes);
        return Array.AsReadOnly(violations.ToArray());
    }

    private static void AddIfExceeded(
        ICollection<PerformanceBudgetViolation> violations,
        PerformanceMetric metric,
        long actual,
        long? limit)
    {
        if (limit is { } value && actual > value)
            violations.Add(new PerformanceBudgetViolation(metric, actual, value));
    }

    private static void ValidateLimit(long? value, string parameterName)
    {
        if (value is < 0)
            throw new ArgumentOutOfRangeException(parameterName, "A performance limit cannot be negative.");
    }
}

public readonly record struct PerformanceBudgetViolation(
    PerformanceMetric Metric,
    long Actual,
    long Limit);

public readonly record struct GpuMemoryEstimate(
    int TextureCount,
    long TextureBytes,
    int RootRenderTargetCount,
    long RootRenderTargetBytes,
    int LeasedRenderTargetCount,
    long LeasedRenderTargetBytes,
    int AvailableRenderTargetCount,
    long AvailableRenderTargetBytes,
    int CustomResourceCount,
    long CustomResourceBytes)
{
    public long TotalBytes => checked(
        TextureBytes +
        RootRenderTargetBytes +
        LeasedRenderTargetBytes +
        AvailableRenderTargetBytes +
        CustomResourceBytes);
}

public sealed record CustomGpuMemoryDiagnostics(string Name, long EstimatedBytes);

/// <summary>一次低频性能采样；只包含值快照，不持有 GPU 资源。</summary>
public sealed record RuntimePerformanceSnapshot(
    DateTimeOffset CapturedAtUtc,
    FrameStatisticsSnapshot? Frame,
    TextureLibraryDiagnostics Textures,
    GpuMemoryEstimate GpuMemory,
    IReadOnlyList<CustomGpuMemoryDiagnostics> CustomResources,
    IReadOnlyList<PerformanceBudgetViolation> BudgetViolations);

public interface IPerformanceTelemetrySink
{
    void Publish(RuntimePerformanceSnapshot snapshot);
}

/// <summary>Hosting 的低频遥测配置；Sink 生命周期仍由调用方拥有。</summary>
public sealed record PerformanceTelemetryOptions
{
    public IPerformanceTelemetrySink Sink { get; }
    public TimeSpan SampleInterval { get; }
    public PerformanceBudget? Budget { get; }

    public PerformanceTelemetryOptions(
        IPerformanceTelemetrySink sink,
        TimeSpan? sampleInterval = null,
        PerformanceBudget? budget = null)
    {
        Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        SampleInterval = sampleInterval ?? TimeSpan.FromSeconds(1);
        if (SampleInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        Budget = budget;
    }
}

internal sealed class PerformanceTelemetrySampler
{
    private readonly PerformanceTelemetryOptions _options;
    private readonly Func<RuntimePerformanceSnapshot> _capture;
    private readonly Func<long> _getTimestamp;
    private readonly double _timestampFrequency;
    private long _lastTimestamp;
    private bool _hasPublished;

    public PerformanceTelemetrySampler(
        PerformanceTelemetryOptions options,
        Func<RuntimePerformanceSnapshot> capture)
        : this(options, capture, Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal PerformanceTelemetrySampler(
        PerformanceTelemetryOptions options,
        Func<RuntimePerformanceSnapshot> capture,
        Func<long> getTimestamp,
        double timestampFrequency)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
        if (!double.IsFinite(timestampFrequency) || timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        _timestampFrequency = timestampFrequency;
    }

    public bool Tick()
    {
        long now = _getTimestamp();
        if (_hasPublished &&
            (now - _lastTimestamp) / _timestampFrequency < _options.SampleInterval.TotalSeconds)
            return false;

        RuntimePerformanceSnapshot snapshot = _capture();
        _options.Sink.Publish(snapshot);
        _lastTimestamp = now;
        _hasPublished = true;
        return true;
    }
}
