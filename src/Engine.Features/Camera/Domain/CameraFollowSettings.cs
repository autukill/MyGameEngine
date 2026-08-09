namespace GameEngine.Features.Camera.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

/// <summary>Immutable gameplay-friendly Camera follow policy.</summary>
public readonly record struct CameraFollowSettings
{
    public Vector2 Anchor { get; }
    public Vector2 DeadZoneSize { get; }
    public float HalfLifeSeconds { get; }
    public Bounds2D? WorldBounds { get; }

    public static CameraFollowSettings Default => new(
        anchor: new Vector2(0.5f),
        deadZoneSize: Vector2.Zero,
        halfLifeSeconds: 0.12f);

    public CameraFollowSettings(
        Vector2 anchor,
        Vector2 deadZoneSize,
        float halfLifeSeconds,
        Bounds2D? worldBounds = null)
    {
        if (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y) ||
            anchor.X < 0f || anchor.X > 1f || anchor.Y < 0f || anchor.Y > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(anchor),
                "Anchor components must be finite values in [0,1].");
        }
        if (!float.IsFinite(deadZoneSize.X) || !float.IsFinite(deadZoneSize.Y) ||
            deadZoneSize.X < 0f || deadZoneSize.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadZoneSize),
                "Dead-zone size must be finite and non-negative.");
        }
        if (!float.IsFinite(halfLifeSeconds) || halfLifeSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(halfLifeSeconds));

        Anchor = anchor;
        DeadZoneSize = deadZoneSize;
        HalfLifeSeconds = halfLifeSeconds;
        WorldBounds = worldBounds;
    }

    internal void Validate() =>
        _ = new CameraFollowSettings(Anchor, DeadZoneSize, HalfLifeSeconds, WorldBounds);
}
