namespace GameEngine.Core.Infrastructure.Diagnostics;

/// <summary>可选帧统计的采样窗口配置。</summary>
public sealed record FrameStatisticsOptions
{
    public double SampleWindowSeconds { get; }

    public FrameStatisticsOptions(double sampleWindowSeconds = 1d)
    {
        if (!double.IsFinite(sampleWindowSeconds) || sampleWindowSeconds <= 0d)
            throw new ArgumentOutOfRangeException(
                nameof(sampleWindowSeconds),
                "The statistics sample window must be finite and positive.");
        SampleWindowSeconds = sampleWindowSeconds;
    }

    public static FrameStatisticsOptions Default { get; } = new();
}

/// <summary>最近完成渲染帧的纯值统计；不持有 GPU 或 Runtime 对象。</summary>
public readonly record struct FrameStatisticsSnapshot(
    long FrameNumber,
    double FramesPerSecond,
    double UpdatesPerSecond,
    int DrawCalls,
    int BatchFlushes,
    int TextureSwitches,
    int ActivePasses);

/// <summary>渲染热路径使用的可空计数入口。</summary>
public interface IFrameStatisticsSink
{
    void RecordUpdate(double deltaTime);
    void BeginRenderFrame(double deltaTime);
    void RecordDrawCall();
    void RecordBatchFlush();
    void RecordTextureSwitch();
    void RecordPassExecuted();
    void EndRenderFrame();
}

/// <summary>向游戏和诊断工具公开的只读帧统计入口。</summary>
public interface IFrameStatisticsProvider
{
    bool TryCapture(out FrameStatisticsSnapshot snapshot);
}

/// <summary>
/// 单窗口线程上的零帧分配采集器。关闭统计时不会创建本对象；开启后快照按值覆盖。
/// </summary>
public sealed class FrameStatisticsCollector : IFrameStatisticsSink, IFrameStatisticsProvider
{
    private readonly double _sampleWindowSeconds;
    private long _frameNumber;
    private int _sampleFrames;
    private int _sampleUpdates;
    private double _renderSampleElapsed;
    private double _updateSampleElapsed;
    private double _framesPerSecond;
    private double _updatesPerSecond;
    private int _drawCalls;
    private int _batchFlushes;
    private int _textureSwitches;
    private int _activePasses;
    private bool _frameOpen;
    private bool _hasSnapshot;
    private FrameStatisticsSnapshot _snapshot;

    public FrameStatisticsCollector(FrameStatisticsOptions? options = null) =>
        _sampleWindowSeconds = (options ?? FrameStatisticsOptions.Default).SampleWindowSeconds;

    void IFrameStatisticsSink.RecordUpdate(double deltaTime)
    {
        if (!IsUsableDelta(deltaTime)) return;
        _sampleUpdates++;
        _updateSampleElapsed += deltaTime;
        _updatesPerSecond = _sampleUpdates / _updateSampleElapsed;
        if (_updateSampleElapsed >= _sampleWindowSeconds)
        {
            _sampleUpdates = 0;
            _updateSampleElapsed = 0d;
        }
    }

    void IFrameStatisticsSink.BeginRenderFrame(double deltaTime)
    {
        if (_frameOpen)
            throw new InvalidOperationException("A render statistics frame is already open.");

        _frameOpen = true;
        _frameNumber++;
        _drawCalls = 0;
        _batchFlushes = 0;
        _textureSwitches = 0;
        _activePasses = 0;

        if (!IsUsableDelta(deltaTime)) return;
        _sampleFrames++;
        _renderSampleElapsed += deltaTime;
        _framesPerSecond = _sampleFrames / _renderSampleElapsed;
        if (_renderSampleElapsed >= _sampleWindowSeconds)
        {
            _sampleFrames = 0;
            _renderSampleElapsed = 0d;
        }
    }

    void IFrameStatisticsSink.RecordDrawCall()
    {
        if (_frameOpen) _drawCalls++;
    }

    void IFrameStatisticsSink.RecordBatchFlush()
    {
        if (_frameOpen) _batchFlushes++;
    }

    void IFrameStatisticsSink.RecordTextureSwitch()
    {
        if (_frameOpen) _textureSwitches++;
    }

    void IFrameStatisticsSink.RecordPassExecuted()
    {
        if (_frameOpen) _activePasses++;
    }

    void IFrameStatisticsSink.EndRenderFrame()
    {
        if (!_frameOpen)
            throw new InvalidOperationException("No render statistics frame is open.");

        _snapshot = new FrameStatisticsSnapshot(
            _frameNumber,
            _framesPerSecond,
            _updatesPerSecond,
            _drawCalls,
            _batchFlushes,
            _textureSwitches,
            _activePasses);
        _hasSnapshot = true;
        _frameOpen = false;
    }

    public bool TryCapture(out FrameStatisticsSnapshot snapshot)
    {
        snapshot = _snapshot;
        return _hasSnapshot;
    }

    private static bool IsUsableDelta(double deltaTime) =>
        double.IsFinite(deltaTime) && deltaTime > 0d;
}
