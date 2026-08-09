namespace GameEngine.Hosting;

using GameEngine.Core.Infrastructure.Windowing;

/// <summary>拥有窗口事件绑定与默认 2D Runtime 的应用宿主。</summary>
public sealed class GameApplication : IDisposable
{
    private readonly GameApplicationPlan _plan;
    private readonly EngineWindow _window;
    private Default2DGameRuntime? _runtime;
    private bool _runStarted;
    private bool _runCompleted;
    private bool _disposed;
    private int _closeRequested;

    internal GameApplication(GameApplicationPlan plan)
    {
        _plan = plan;
        _window = new EngineWindow(plan.WindowOptions);
        _window.OnLoad += HandleLoad;
        _window.OnStep += HandleStep;
        _window.OnDraw += HandleDraw;
        _window.OnResize += HandleResize;
        _window.OnClosing += HandleClosing;
    }

    public static GameApplicationBuilder Create(EngineWindowOptions? options = null) =>
        new(options ?? EngineWindowOptions.Default);

    public Default2DGameContext Context =>
        _runtime?.Context ?? throw new InvalidOperationException(
            "Game context is available after the window Load event.");

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runStarted)
            throw new InvalidOperationException("GameApplication can only run once.");
        _runStarted = true;
        try
        {
            _window.Run();
        }
        finally
        {
            _runCompleted = true;
            DisposeRuntime();
        }
    }

    public void Close() => Interlocked.Exchange(ref _closeRequested, 1);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_runStarted && !_runCompleted)
        {
            Close();
            return;
        }
        DisposeRuntime();
    }

    private void HandleLoad()
    {
        _runtime = Default2DGameRuntime.Create(_window, _plan, Close);
        FlushCloseRequest();
    }

    private void HandleStep(double deltaTime)
    {
        _runtime!.Step(deltaTime);
        FlushCloseRequest();
    }

    private void HandleDraw()
    {
        _runtime!.Draw();
        FlushCloseRequest();
    }

    private void HandleResize(int width, int height) => _runtime?.Resize(width, height);

    private void HandleClosing() => DisposeRuntime();

    private void DisposeRuntime()
    {
        Interlocked.Exchange(ref _runtime, null)?.Dispose();
    }

    private void FlushCloseRequest()
    {
        if (Interlocked.Exchange(ref _closeRequested, 0) != 0)
            _window.NativeWindow.Close();
    }
}
