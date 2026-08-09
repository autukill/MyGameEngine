namespace GameEngine.Core.Infrastructure.Windowing;

using Silk.NET.Maths;
using Silk.NET.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;

public record EngineWindowOptions(
    string Title = "Custom C# 2D Engine",
    Vector2D<int> Size = default,
    bool VSync = true,
    int StencilBits = 8,
    int DepthBits = 24,
    bool IsVisible = true,
    double FramesPerSecond = 0,
    double UpdatesPerSecond = 0,
    double? FixedDeltaTime = null,
    FrameStatisticsOptions? FrameStatistics = null)
{
    public static EngineWindowOptions Default => new(
        Size: new Vector2D<int>(1280, 720));

    public WindowOptions ToSilkWindowOptions()
    {
        FrameRateSettings frameRate = GetFrameRate();
        var opts = WindowOptions.Default;
        opts.Title = Title;
        opts.Size = Size;
        opts.VSync = frameRate.VSync;
        opts.IsVisible = IsVisible;
        opts.FramesPerSecond = frameRate.FramesPerSecond;
        opts.UpdatesPerSecond = frameRate.UpdatesPerSecond;

        // 配置 OpenGL 3.3 Core Profile
        opts.API = new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));

        // 显式向驱动申请 Depth 与 Stencil Buffer 位深
        opts.PreferredDepthBufferBits = DepthBits;
        opts.PreferredStencilBufferBits = StencilBits;

        return opts;
    }

    public FrameRateSettings GetFrameRate() =>
        new(FramesPerSecond, UpdatesPerSecond, VSync);

    public EngineWindowOptions WithFrameRate(FrameRateSettings settings) => this with
    {
        VSync = settings.VSync,
        FramesPerSecond = settings.FramesPerSecond,
        UpdatesPerSecond = settings.UpdatesPerSecond
    };

    public EngineWindowOptions WithFrameStatistics(FrameStatisticsOptions? options = null) =>
        this with { FrameStatistics = options ?? FrameStatisticsOptions.Default };
}
