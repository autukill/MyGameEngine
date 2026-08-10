namespace GameEngine.Features.TransformHierarchy.Domain;

using System.Numerics;

/// <summary>
/// A node's transform relative to its parent. Rotation uses radians and is visually
/// counter-clockwise in the engine's screen coordinate system where positive Y points down.
/// </summary>
public readonly record struct LocalTransform2D(
    Vector2 Position,
    float RotationRadians,
    Vector2 Scale)
{
    public static LocalTransform2D Identity { get; } = new(
        Vector2.Zero,
        0f,
        Vector2.One);

    /// <summary>
    /// Builds the row-vector matrix Scale * Rotation(-radians) * Translation.
    /// </summary>
    public Matrix3x2 ToMatrix()
    {
        Validate(this, nameof(LocalTransform2D));
        return Matrix3x2.CreateScale(Scale) *
               Matrix3x2.CreateRotation(-RotationRadians) *
               Matrix3x2.CreateTranslation(Position);
    }

    internal static void Validate(in LocalTransform2D transform, string parameterName)
    {
        if (!float.IsFinite(transform.Position.X) ||
            !float.IsFinite(transform.Position.Y) ||
            !float.IsFinite(transform.RotationRadians) ||
            !float.IsFinite(transform.Scale.X) ||
            !float.IsFinite(transform.Scale.Y))
        {
            throw new ArgumentException(
                "Transform position, rotation, and scale must be finite.",
                parameterName);
        }
    }
}
