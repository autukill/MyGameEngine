namespace GameEngine.Testing.Visual;

using Silk.NET.Maths;
using GameEngine.Core.Infrastructure.Windowing;

public readonly record struct VisualCheckpoint
{
    public VisualCheckpoint(int frameIndex, string name)
    {
        if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        FrameIndex = frameIndex;
        Name = name;
    }

    public int FrameIndex { get; }
    public string Name { get; }
}

public interface IVisualRegressionScenario : IDisposable
{
    string Name { get; }
    int Width { get; }
    int Height { get; }
    int FrameCount { get; }
    IReadOnlyList<VisualCheckpoint> Checkpoints { get; }
    void Initialize(EngineWindow window);
    void AdvanceAndDraw(int frameIndex, double fixedDeltaTime);
}

public readonly record struct VisualRegressionHostOptions(
    bool IsVisible = false,
    double FixedDeltaTime = 1d / 60d)
{
    public static VisualRegressionHostOptions Default => new(false, 1d / 60d);
}

public sealed record VisualCapture(string Scenario, string Checkpoint, CapturedFrame Frame)
{
    public string Id => $"{Scenario}.{Checkpoint}";
}

public sealed class VisualGraphicsUnavailableException : Exception
{
    public VisualGraphicsUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}

public static class VisualRegressionHost
{
    public static IReadOnlyList<VisualCapture> Run(
        IVisualRegressionScenario scenario,
        VisualRegressionHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        VisualRegressionHostOptions hostOptions = options ?? VisualRegressionHostOptions.Default;
        if (scenario.Width <= 0 || scenario.Height <= 0 || scenario.FrameCount <= 0)
            throw new ArgumentException("Scenario dimensions and frame count must be positive.", nameof(scenario));
        if (!double.IsFinite(hostOptions.FixedDeltaTime) || hostOptions.FixedDeltaTime <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));

        var checkpoints = scenario.Checkpoints.ToDictionary(
            checkpoint => checkpoint.FrameIndex,
            checkpoint => checkpoint.Name);
        if (checkpoints.Keys.Any(frame => frame >= scenario.FrameCount))
            throw new ArgumentException("Checkpoint frame exceeds scenario frame count.", nameof(scenario));

        var captures = new List<VisualCapture>(checkpoints.Count);
        bool loaded = false;
        bool disposed = false;
        int frameIndex = 0;
        Exception? callbackError = null;
        EngineWindow? window = null;

        try
        {
            window = new EngineWindow(new EngineWindowOptions(
                Title: $"Visual Regression: {scenario.Name}",
                Size: new Vector2D<int>(scenario.Width, scenario.Height),
                VSync: false,
                IsVisible: hostOptions.IsVisible,
                FramesPerSecond: 0,
                UpdatesPerSecond: 0,
                FixedDeltaTime: hostOptions.FixedDeltaTime));

            window.OnLoad += () =>
            {
                try
                {
                    scenario.Initialize(window);
                    loaded = true;
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                    window.NativeWindow.Close();
                }
            };
            window.OnDraw += () =>
            {
                if (callbackError is not null) return;
                try
                {
                    scenario.AdvanceAndDraw(frameIndex, hostOptions.FixedDeltaTime);
                    if (checkpoints.TryGetValue(frameIndex, out string? checkpoint))
                    {
                        captures.Add(new VisualCapture(
                            scenario.Name,
                            checkpoint,
                            FramebufferCapture.Capture(
                                window.Graphics.Gl,
                                window.Width,
                                window.Height)));
                    }
                    frameIndex++;
                    if (frameIndex >= scenario.FrameCount)
                        window.NativeWindow.Close();
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                    window.NativeWindow.Close();
                }
            };
            window.OnClosing += () =>
            {
                if (disposed) return;
                disposed = true;
                scenario.Dispose();
            };

            window.Run();
        }
        catch (Exception exception) when (!loaded)
        {
            throw new VisualGraphicsUnavailableException(
                $"OpenGL context is unavailable for scenario '{scenario.Name}'.",
                exception);
        }
        finally
        {
            if (!disposed) scenario.Dispose();
        }

        if (callbackError is not null) throw callbackError;
        if (captures.Count != checkpoints.Count)
            throw new InvalidOperationException(
                $"Scenario '{scenario.Name}' captured {captures.Count}/{checkpoints.Count} checkpoints.");
        return captures;
    }
}
