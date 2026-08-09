namespace GameEngine.Features.Camera.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Per-Camera gameplay controller for anchor/dead-zone following, half-life smoothing, world
/// constraints, and additive shake requests. It owns no Scene or rendering resources.
/// </summary>
public sealed class CameraFollowController
{
    private CameraFollowSettings _settings;

    public Camera2D Camera { get; }

    public CameraFollowSettings Settings
    {
        get => _settings;
        set
        {
            value.Validate();
            _settings = value;
        }
    }

    public CameraFollowController(
        Camera2D camera,
        CameraFollowSettings settings)
    {
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        settings.Validate();
        _settings = settings;
    }

    public CameraFollowController(Camera2D camera)
        : this(camera, CameraFollowSettings.Default)
    {
    }

    public void SnapTo(GameInstance target)
    {
        ArgumentNullException.ThrowIfNull(target);
        SnapTo(ToNumerics(target.Position));
    }

    public void SnapTo(Vector2 targetWorld)
    {
        ValidateTarget(targetWorld);
        Camera.Position = ResolveDesiredPosition(targetWorld, useDeadZone: false);
        ConstrainToWorldBounds();
    }

    public void Update(GameInstance target, double deltaTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        Update(ToNumerics(target.Position), deltaTime);
    }

    public void Update(Vector2 targetWorld, double deltaTime)
    {
        ValidateTarget(targetWorld);
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        Vector2 desired = ResolveDesiredPosition(targetWorld, useDeadZone: true);
        Vector2D smoothed = Motion.Damp(
            ToEngine(Camera.Position),
            ToEngine(desired),
            Settings.HalfLifeSeconds,
            deltaTime);
        Camera.Position = ToNumerics(smoothed);
        ConstrainToWorldBounds();
    }

    public void AddShake(float magnitude, float durationSeconds) =>
        Camera.AddShake(magnitude, durationSeconds);

    private Vector2 ResolveDesiredPosition(Vector2 targetWorld, bool useDeadZone)
    {
        Vector2 anchorViewport = Settings.Anchor * Camera.ViewportSize;
        Vector2 desiredViewport = anchorViewport;
        if (useDeadZone && Settings.DeadZoneSize != Vector2.Zero)
        {
            Vector2 targetViewport = Camera.WorldToViewport(targetWorld);
            Vector2 halfDeadZone = Settings.DeadZoneSize * 0.5f;
            desiredViewport = Vector2.Clamp(
                targetViewport,
                anchorViewport - halfDeadZone,
                anchorViewport + halfDeadZone);
            if (desiredViewport == targetViewport)
                return Camera.Position;
        }

        if (!Camera.TryViewportToWorld(desiredViewport, out Vector2 boundaryWorld))
            return Camera.Position;
        return Camera.Position + targetWorld - boundaryWorld;
    }

    private void ConstrainToWorldBounds()
    {
        if (Settings.WorldBounds is not { } worldBounds ||
            !Camera.TryGetStableVisibleWorldBounds(out Bounds2D viewBounds))
        {
            return;
        }

        float offsetX = ResolveConstraintOffset(
            viewBounds.Left,
            viewBounds.Right,
            worldBounds.Left,
            worldBounds.Right);
        float offsetY = ResolveConstraintOffset(
            viewBounds.Top,
            viewBounds.Bottom,
            worldBounds.Top,
            worldBounds.Bottom);
        Camera.Position += new Vector2(offsetX, offsetY);
    }

    private static float ResolveConstraintOffset(
        float viewMinimum,
        float viewMaximum,
        float worldMinimum,
        float worldMaximum)
    {
        float viewSize = viewMaximum - viewMinimum;
        float worldSize = worldMaximum - worldMinimum;
        if (viewSize >= worldSize)
            return (worldMinimum + worldMaximum - viewMinimum - viewMaximum) * 0.5f;
        if (viewMinimum < worldMinimum)
            return worldMinimum - viewMinimum;
        if (viewMaximum > worldMaximum)
            return worldMaximum - viewMaximum;
        return 0f;
    }

    private static void ValidateTarget(Vector2 targetWorld)
    {
        if (!float.IsFinite(targetWorld.X) || !float.IsFinite(targetWorld.Y))
            throw new ArgumentOutOfRangeException(nameof(targetWorld));
    }

    private static Vector2D ToEngine(Vector2 value) => new(value.X, value.Y);
    private static Vector2 ToNumerics(Vector2D value) => new(value.X, value.Y);
}
