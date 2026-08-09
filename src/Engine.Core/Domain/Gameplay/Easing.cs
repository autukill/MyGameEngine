namespace GameEngine.Core.Domain.Gameplay;

/// <summary>Stateless normalized easing functions. Input progress is clamped to [0, 1].</summary>
public static class Easing
{
    private const float BackC1 = 1.70158f;
    private const float BackC2 = BackC1 * 1.525f;
    private const float BackC3 = BackC1 + 1f;

    public static float Evaluate(EasingKind kind, double progress)
    {
        if (!double.IsFinite(progress))
            throw new ArgumentOutOfRangeException(nameof(progress), progress, "Progress must be finite.");

        float t = (float)Math.Clamp(progress, 0d, 1d);
        return kind switch
        {
            EasingKind.Linear => t,
            EasingKind.SmoothStep => t * t * (3f - 2f * t),
            EasingKind.SmootherStep => t * t * t * (t * (t * 6f - 15f) + 10f),
            EasingKind.SineIn => 1f - MathF.Cos(t * MathF.PI * .5f),
            EasingKind.SineOut => MathF.Sin(t * MathF.PI * .5f),
            EasingKind.SineInOut => -(MathF.Cos(MathF.PI * t) - 1f) * .5f,
            EasingKind.QuadIn => t * t,
            EasingKind.QuadOut => 1f - Square(1f - t),
            EasingKind.QuadInOut => t < .5f
                ? 2f * t * t
                : 1f - Square(-2f * t + 2f) * .5f,
            EasingKind.CubicIn => t * t * t,
            EasingKind.CubicOut => 1f - Cube(1f - t),
            EasingKind.CubicInOut => t < .5f
                ? 4f * t * t * t
                : 1f - Cube(-2f * t + 2f) * .5f,
            EasingKind.ExpoIn => t == 0f ? 0f : MathF.Pow(2f, 10f * t - 10f),
            EasingKind.ExpoOut => t == 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
            EasingKind.ExpoInOut => ExpoInOut(t),
            EasingKind.BackIn => BackC3 * t * t * t - BackC1 * t * t,
            EasingKind.BackOut => 1f + BackC3 * Cube(t - 1f) + BackC1 * Square(t - 1f),
            EasingKind.BackInOut => BackInOut(t),
            EasingKind.BounceIn => 1f - BounceOut(1f - t),
            EasingKind.BounceOut => BounceOut(t),
            EasingKind.BounceInOut => t < .5f
                ? (1f - BounceOut(1f - 2f * t)) * .5f
                : (1f + BounceOut(2f * t - 1f)) * .5f,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown easing kind.")
        };
    }

    private static float ExpoInOut(float t)
    {
        if (t == 0f || t == 1f) return t;
        return t < .5f
            ? MathF.Pow(2f, 20f * t - 10f) * .5f
            : (2f - MathF.Pow(2f, -20f * t + 10f)) * .5f;
    }

    private static float BackInOut(float t) => t < .5f
        ? Square(2f * t) * ((BackC2 + 1f) * 2f * t - BackC2) * .5f
        : (Square(2f * t - 2f) * ((BackC2 + 1f) * (2f * t - 2f) + BackC2) + 2f) * .5f;

    private static float BounceOut(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1)
        {
            t -= 1.5f / d1;
            return n1 * t * t + .75f;
        }
        if (t < 2.5f / d1)
        {
            t -= 2.25f / d1;
            return n1 * t * t + .9375f;
        }

        t -= 2.625f / d1;
        return n1 * t * t + .984375f;
    }

    private static float Square(float value) => value * value;
    private static float Cube(float value) => value * value * value;
}
