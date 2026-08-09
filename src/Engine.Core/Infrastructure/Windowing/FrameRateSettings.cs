namespace GameEngine.Core.Infrastructure.Windowing;

/// <summary>窗口循环的渲染与更新节流目标；0 表示不由窗口循环限速。</summary>
public readonly record struct FrameRateSettings
{
    public double FramesPerSecond { get; }
    public double UpdatesPerSecond { get; }
    public bool VSync { get; }

    public FrameRateSettings(
        double framesPerSecond = 0,
        double updatesPerSecond = 0,
        bool vSync = true)
    {
        ValidateRate(framesPerSecond, nameof(framesPerSecond));
        ValidateRate(updatesPerSecond, nameof(updatesPerSecond));
        FramesPerSecond = framesPerSecond;
        UpdatesPerSecond = updatesPerSecond;
        VSync = vSync;
    }

    public static FrameRateSettings Default => new(0d, 0d, vSync: true);

    public static FrameRateSettings Uncapped => new(vSync: false);

    private static void ValidateRate(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Frame and update rates must be finite and non-negative; 0 means unlimited.");
    }
}
