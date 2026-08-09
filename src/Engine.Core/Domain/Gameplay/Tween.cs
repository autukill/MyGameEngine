namespace GameEngine.Core.Domain.Gameplay;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>Stateless interpolation helpers built on normalized easing curves.</summary>
public static class Tween
{
    public static float Progress(
        double elapsed,
        double duration)
    {
        if (!double.IsFinite(elapsed))
            throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Elapsed time must be finite.");
        if (!double.IsFinite(duration) || duration <= 0d)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be finite and greater than zero.");

        return (float)Math.Clamp(elapsed / duration, 0d, 1d);
    }

    public static float EasedProgress(
        double elapsed,
        double duration,
        EasingKind easing) =>
        Easing.Evaluate(easing, Progress(elapsed, duration));

    public static float Lerp(
        float from,
        float to,
        double progress,
        EasingKind easing = EasingKind.Linear) =>
        from + (to - from) * Easing.Evaluate(easing, progress);

    public static float Lerp(
        float from,
        float to,
        double elapsed,
        double duration,
        EasingKind easing = EasingKind.Linear) =>
        Lerp(from, to, Progress(elapsed, duration), easing);

    public static Vector2D Lerp(
        Vector2D from,
        Vector2D to,
        double progress,
        EasingKind easing = EasingKind.Linear) =>
        from + (to - from) * Easing.Evaluate(easing, progress);

    public static Vector2D Lerp(
        Vector2D from,
        Vector2D to,
        double elapsed,
        double duration,
        EasingKind easing = EasingKind.Linear) =>
        Lerp(from, to, Progress(elapsed, duration), easing);

    public static Vector2 Lerp(
        Vector2 from,
        Vector2 to,
        double progress,
        EasingKind easing = EasingKind.Linear) =>
        Vector2.Lerp(from, to, Easing.Evaluate(easing, progress));

    public static Vector2 Lerp(
        Vector2 from,
        Vector2 to,
        double elapsed,
        double duration,
        EasingKind easing = EasingKind.Linear) =>
        Lerp(from, to, Progress(elapsed, duration), easing);

    public static Vector4 Lerp(
        Vector4 from,
        Vector4 to,
        double progress,
        EasingKind easing = EasingKind.Linear) =>
        Vector4.Lerp(from, to, Easing.Evaluate(easing, progress));

    public static Vector4 Lerp(
        Vector4 from,
        Vector4 to,
        double elapsed,
        double duration,
        EasingKind easing = EasingKind.Linear) =>
        Lerp(from, to, Progress(elapsed, duration), easing);

    public static float AngleRadians(
        float from,
        float to,
        double progress,
        EasingKind easing = EasingKind.Linear)
    {
        float delta = MathF.IEEERemainder(to - from, MathF.Tau);
        return from + delta * Easing.Evaluate(easing, progress);
    }

    public static float AngleRadians(
        float from,
        float to,
        double elapsed,
        double duration,
        EasingKind easing = EasingKind.Linear) =>
        AngleRadians(from, to, Progress(elapsed, duration), easing);
}
