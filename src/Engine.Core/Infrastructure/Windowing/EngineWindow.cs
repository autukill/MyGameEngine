namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Core.Infrastructure.Input;
using GameEngine.Core.Infrastructure.Diagnostics;

/// <summary>
/// 引擎主窗口：把 Silk.NET 的 Update/Render 事件拆解为
/// 类 GameMaker 的生命周期管道：PreStep → Step → PostStep → DrawBegin → Draw → DrawGUI
/// </summary>
public class EngineWindow
{
    private readonly IWindow _nativeWindow;
    private readonly double? _fixedDeltaTime;
    private readonly FrameStatisticsCollector? _frameStatistics;
    private FrameRateSettings _frameRate;
    public GraphicsDevice Graphics { get; private set; } = null!;

    /// <summary>
    /// 输入系统（缓存键盘/鼠标设备 + 每帧沿事件缓冲）。
    /// 在 OnLoad 之前初始化，组合根可在 OnLoad 中 scene.SetInput(window.Input)。
    /// </summary>
    public InputSystem Input { get; private set; } = null!;

    public event Action<double>? OnPreStep;
    public event Action<double>? OnStep;
    public event Action<double>? OnPostStep;

    public event Action? OnLoad;
    public event Action? OnDrawBegin;
    public event Action? OnDraw;
    public event Action? OnDrawGUI;
    public event Action? OnFrameCompleted;
    public event Action<int, int>? OnResize;
    public event Action? OnClosing;

    static EngineWindow()
    {
        GlfwWindowing.Use();
        InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
        GlfwInput.RegisterPlatform();
    }

    public EngineWindow(EngineWindowOptions options)
    {
        _frameRate = options.GetFrameRate();
        if (options.FixedDeltaTime is { } fixedDeltaTime &&
            (!double.IsFinite(fixedDeltaTime) || fixedDeltaTime <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Fixed delta time must be finite and positive.");
        }
        _fixedDeltaTime = options.FixedDeltaTime;
        _frameStatistics = options.FrameStatistics is null
            ? null
            : new FrameStatisticsCollector(options.FrameStatistics);
        _nativeWindow = Window.Create(options.ToSilkWindowOptions());
        _nativeWindow.Load += HandleLoad;
        _nativeWindow.Update += HandleUpdate;
        _nativeWindow.Render += HandleRender;
        _nativeWindow.Resize += HandleResize;
        _nativeWindow.Closing += HandleClosing;
    }

    public int Width => _nativeWindow.Size.X;
    public int Height => _nativeWindow.Size.Y;
    public FrameRateSettings FrameRate => _frameRate;
    public IFrameStatisticsProvider? FrameStatistics => _frameStatistics;
    public IFrameStatisticsSink? FrameStatisticsSink => _frameStatistics;

    /// <summary>暴露原生 Silk.NET IWindow 给上层做 Input 等扩展</summary>
    public Silk.NET.Windowing.IWindow NativeWindow => _nativeWindow;

    public void Run() => _nativeWindow.Run();

    /// <summary>在窗口线程上立即更新 VSync、渲染 FPS 与更新 UPS 目标。</summary>
    public void SetFrameRate(FrameRateSettings settings)
    {
        _nativeWindow.VSync = settings.VSync;
        _nativeWindow.FramesPerSecond = settings.FramesPerSecond;
        _nativeWindow.UpdatesPerSecond = settings.UpdatesPerSecond;
        _frameRate = settings;
    }

    private void HandleLoad()
    {
        Graphics = new GraphicsDevice(_nativeWindow);
        // 注: Silk.NET 2.22 中 StringName 枚举可见性因平台而异，这里跳过版本字符串输出
        Input = new InputSystem(_nativeWindow.CreateInput());
        OnLoad?.Invoke();
    }

    private void HandleUpdate(double deltaTime)
    {
        ((IFrameStatisticsSink?)_frameStatistics)?.RecordUpdate(deltaTime);
        deltaTime = _fixedDeltaTime ?? deltaTime;
        Input?.BeginFrame();
        OnPreStep?.Invoke(deltaTime);
        OnStep?.Invoke(deltaTime);
        OnPostStep?.Invoke(deltaTime);
    }

    private void HandleRender(double deltaTime)
    {
        IFrameStatisticsSink? statistics = _frameStatistics;
        statistics?.BeginRenderFrame(deltaTime);
        try
        {
            Graphics.ClearBuffers();
            OnDrawBegin?.Invoke();
            OnDraw?.Invoke();
            OnDrawGUI?.Invoke();
        }
        finally
        {
            statistics?.EndRenderFrame();
        }
        OnFrameCompleted?.Invoke();
    }

    private void HandleResize(Vector2D<int> size)
    {
        Graphics?.OnResize(size.X, size.Y);
        OnResize?.Invoke(size.X, size.Y);
    }

    private void HandleClosing()
    {
        try
        {
            OnClosing?.Invoke();
        }
        finally
        {
            Input?.Dispose();
            Graphics?.Dispose();
        }
    }
}
