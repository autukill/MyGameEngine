namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.ValueObjects;

public enum CollisionShapeKind
{
    Box,
    Circle
}

/// <summary>
/// Lightweight local-space collider. Box colliders remain world-axis-aligned; scale is applied,
/// while Transform rotation intentionally does not rotate collision geometry in v1.
/// </summary>
public readonly record struct CollisionShape2D
{
    public CollisionShapeKind Kind { get; }
    public Vector2D Offset { get; }
    public Vector2D Size { get; }
    public float Radius { get; }

    private CollisionShape2D(
        CollisionShapeKind kind,
        Vector2D offset,
        Vector2D size,
        float radius)
    {
        Kind = kind;
        Offset = offset;
        Size = size;
        Radius = radius;
    }

    public static CollisionShape2D Box(float width, float height, Vector2D offset = default)
    {
        if (!float.IsFinite(width) || width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        ValidateOffset(offset);
        return new CollisionShape2D(
            CollisionShapeKind.Box,
            offset,
            new Vector2D(width, height),
            0f);
    }

    public static CollisionShape2D Circle(float radius, Vector2D offset = default)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        ValidateOffset(offset);
        return new CollisionShape2D(
            CollisionShapeKind.Circle,
            offset,
            Vector2D.Zero,
            radius);
    }

    private static void ValidateOffset(Vector2D offset)
    {
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            throw new ArgumentOutOfRangeException(nameof(offset));
    }
}

public readonly record struct Bounds2D
{
    public float Left { get; }
    public float Top { get; }
    public float Right { get; }
    public float Bottom { get; }

    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public Vector2D Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);

    public Bounds2D(float left, float top, float right, float bottom)
    {
        if (!float.IsFinite(left) || !float.IsFinite(top) ||
            !float.IsFinite(right) || !float.IsFinite(bottom) ||
            right < left || bottom < top)
        {
            throw new ArgumentException("Bounds must be finite and ordered.");
        }
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static Bounds2D FromCenter(Vector2D center, Vector2D size)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(size.X) || !float.IsFinite(size.Y) ||
            size.X < 0f || size.Y < 0f)
        {
            throw new ArgumentException("Center and size must be finite; size cannot be negative.");
        }
        Vector2D half = size * 0.5f;
        return new Bounds2D(
            center.X - half.X,
            center.Y - half.Y,
            center.X + half.X,
            center.Y + half.Y);
    }

    public bool Intersects(Bounds2D other) =>
        Left <= other.Right && Right >= other.Left &&
        Top <= other.Bottom && Bottom >= other.Top;

    public bool Contains(Vector2D point) =>
        point.X >= Left && point.X <= Right &&
        point.Y >= Top && point.Y <= Bottom;
}

public static class CollisionMath2D
{
    public static Bounds2D GetBounds(CollisionShape2D shape, Transform2D transform)
    {
        Vector2D center = GetCenter(shape, transform);
        float scaleX = MathF.Abs(transform.Scale.X);
        float scaleY = MathF.Abs(transform.Scale.Y);
        return shape.Kind switch
        {
            CollisionShapeKind.Box => Bounds2D.FromCenter(
                center,
                new Vector2D(shape.Size.X * scaleX, shape.Size.Y * scaleY)),
            CollisionShapeKind.Circle => Bounds2D.FromCenter(
                center,
                Vector2D.One * (shape.Radius * 2f * MathF.Max(scaleX, scaleY))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    public static bool Intersects(
        CollisionShape2D first,
        Transform2D firstTransform,
        CollisionShape2D second,
        Transform2D secondTransform)
    {
        if (first.Kind == CollisionShapeKind.Box && second.Kind == CollisionShapeKind.Box)
            return GetBounds(first, firstTransform).Intersects(GetBounds(second, secondTransform));
        if (first.Kind == CollisionShapeKind.Circle && second.Kind == CollisionShapeKind.Circle)
        {
            Vector2D delta = GetCenter(first, firstTransform) - GetCenter(second, secondTransform);
            float radius = GetRadius(first, firstTransform) + GetRadius(second, secondTransform);
            return delta.LengthSquared() <= radius * radius;
        }

        return first.Kind == CollisionShapeKind.Circle
            ? CircleIntersectsBox(first, firstTransform, second, secondTransform)
            : CircleIntersectsBox(second, secondTransform, first, firstTransform);
    }

    private static bool CircleIntersectsBox(
        CollisionShape2D circle,
        Transform2D circleTransform,
        CollisionShape2D box,
        Transform2D boxTransform)
    {
        Vector2D center = GetCenter(circle, circleTransform);
        Bounds2D bounds = GetBounds(box, boxTransform);
        float closestX = Math.Clamp(center.X, bounds.Left, bounds.Right);
        float closestY = Math.Clamp(center.Y, bounds.Top, bounds.Bottom);
        float dx = center.X - closestX;
        float dy = center.Y - closestY;
        float radius = GetRadius(circle, circleTransform);
        return dx * dx + dy * dy <= radius * radius;
    }

    private static Vector2D GetCenter(CollisionShape2D shape, Transform2D transform) =>
        transform.Position + new Vector2D(
            shape.Offset.X * transform.Scale.X,
            shape.Offset.Y * transform.Scale.Y);

    private static float GetRadius(CollisionShape2D shape, Transform2D transform) =>
        shape.Radius * MathF.Max(MathF.Abs(transform.Scale.X), MathF.Abs(transform.Scale.Y));
}
