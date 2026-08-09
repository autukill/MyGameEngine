namespace GameEngine.Features.StencilMasking.Domain;

using GameEngine.Core.Domain.ValueObjects;

public enum StencilMaskGeometryKind
{
    Circle,
    SpriteAlpha
}

/// <summary>不含 GPU 对象的二值 Stencil 几何。</summary>
public readonly record struct StencilMaskGeometry
{
    public StencilMaskGeometryKind Kind { get; }
    public Vector2D Center { get; }
    public float Radius { get; }
    public SpriteRef Sprite { get; }
    public float SubImage { get; }
    public Transform2D Transform { get; }
    public float AlphaCutoff { get; }
    public bool IsValid => Kind switch
    {
        StencilMaskGeometryKind.Circle => Radius > 0f,
        StencilMaskGeometryKind.SpriteAlpha => !Sprite.IsEmpty,
        _ => false
    };

    private StencilMaskGeometry(
        StencilMaskGeometryKind kind,
        Vector2D center,
        float radius,
        SpriteRef sprite,
        float subImage,
        Transform2D transform,
        float alphaCutoff)
    {
        Kind = kind;
        Center = center;
        Radius = radius;
        Sprite = sprite;
        SubImage = subImage;
        Transform = transform;
        AlphaCutoff = alphaCutoff;
    }

    public static StencilMaskGeometry Circle(Vector2D center, float radius)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
            throw new ArgumentException("Circle center must be finite.", nameof(center));
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius));
        return new StencilMaskGeometry(
            StencilMaskGeometryKind.Circle,
            center,
            radius,
            SpriteRef.Empty,
            0f,
            Transform2D.Default,
            0.5f);
    }

    public static StencilMaskGeometry FromSprite(
        SpriteRef sprite,
        float subImage,
        Transform2D transform,
        float alphaCutoff = 0.5f)
    {
        if (sprite.IsEmpty)
            throw new ArgumentException("Mask Sprite cannot be empty.", nameof(sprite));
        if (!float.IsFinite(subImage))
            throw new ArgumentOutOfRangeException(nameof(subImage));
        if (!float.IsFinite(transform.Position.X) || !float.IsFinite(transform.Position.Y) ||
            !float.IsFinite(transform.Rotation) ||
            !float.IsFinite(transform.Scale.X) || !float.IsFinite(transform.Scale.Y) ||
            transform.Scale.X == 0f || transform.Scale.Y == 0f)
            throw new ArgumentException(
                "Mask Sprite transform must be finite and have non-zero scale.", nameof(transform));
        if (!float.IsFinite(alphaCutoff) || alphaCutoff is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(alphaCutoff));
        return new StencilMaskGeometry(
            StencilMaskGeometryKind.SpriteAlpha,
            Vector2D.Zero,
            0f,
            sprite,
            subImage,
            transform,
            alphaCutoff);
    }
}
