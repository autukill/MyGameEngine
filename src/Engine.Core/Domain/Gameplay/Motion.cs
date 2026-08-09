namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>Frame-rate independent helpers for continuously approaching a target.</summary>
public static class Motion
{
    private const float Ln2 = 0.6931471805599453f;

    public static float MoveTowards(float current, float target, double maxDelta)
    {
        ValidateNonNegativeFinite(maxDelta, nameof(maxDelta));
        float delta = target - current;
        return MathF.Abs(delta) <= maxDelta
            ? target
            : current + (float)Math.CopySign(maxDelta, delta);
    }

    public static Vector2D MoveTowards(Vector2D current, Vector2D target, double maxDistance)
    {
        ValidateNonNegativeFinite(maxDistance, nameof(maxDistance));
        Vector2D delta = target - current;
        float distance = delta.Length();
        return distance <= maxDistance || distance == 0f
            ? target
            : current + delta * (float)(maxDistance / distance);
    }

    public static float Damp(float current, float target, double halfLife, double deltaTime) =>
        current + (target - current) * DampFactor(halfLife, deltaTime);

    public static Vector2D Damp(
        Vector2D current,
        Vector2D target,
        double halfLife,
        double deltaTime) =>
        current + (target - current) * DampFactor(halfLife, deltaTime);

    public static float DampAngleRadians(
        float current,
        float target,
        double halfLife,
        double deltaTime)
    {
        float delta = MathF.IEEERemainder(target - current, MathF.Tau);
        return current + delta * DampFactor(halfLife, deltaTime);
    }

    private static float DampFactor(double halfLife, double deltaTime)
    {
        ValidateNonNegativeFinite(halfLife, nameof(halfLife));
        ValidateNonNegativeFinite(deltaTime, nameof(deltaTime));
        if (deltaTime == 0f) return 0f;
        if (halfLife == 0f) return 1f;
        return 1f - MathF.Exp((float)(-Ln2 * deltaTime / halfLife));
    }

    private static void ValidateNonNegativeFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Value must be finite and non-negative.");
    }
}
