namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.Camera.Domain;

/// <summary>
/// Interactive world observer bound to one Camera2D. It owns navigation state and plugins, but no
/// Scene, RenderTarget, world content, or texture resources.
/// </summary>
public sealed class ViewportController
{
    private Vector2 _observedPosition;
    private Vector2 _observedViewportSize;
    private Vector2 _observedCenter;
    private float _observedZoom;

    internal bool UserInteractionStarted { get; private set; }
    internal int ActivePointerCount { get; private set; }
    internal bool DragActive { get; set; }
    internal bool DragReleased { get; set; }
    internal Vector2 ReleasedVelocity { get; set; }
    internal Vector2? ZoomAnchor { get; private set; }

    public Camera2D Camera { get; }
    public ViewportPluginManager Plugins { get; }
    public ulong Revision { get; private set; }
    public Vector2 ScreenSize => Camera.ViewportSize;
    public float Zoom => Camera.Zoom;
    public Vector2 Position => Camera.Position;
    public Bounds2D VisibleWorldBounds => TryGetVisibleWorldBounds();
    public Vector2 Center => ToNumerics(VisibleWorldBounds.Center);
    public float VisibleWorldWidth => Camera.ViewportSize.X / Camera.Zoom;
    public float VisibleWorldHeight => Camera.ViewportSize.Y / Camera.Zoom;

    public event Action<ViewportChangedEvent>? Changed;

    public ViewportController(Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ValidateCamera(camera);
        Camera = camera;
        Plugins = new ViewportPluginManager(this);
        ObserveCamera();
    }

    public void Update(in ViewportInputFrame input, double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        DetectExternalCameraChange();
        UserInteractionStarted = false;
        ActivePointerCount = 0;
        DragActive = false;
        DragReleased = false;
        ReleasedVelocity = Vector2.Zero;
        ZoomAnchor = null;
        ReadOnlySpan<ViewportPointer> pointers = input.Pointers;
        for (int i = 0; i < pointers.Length; i++)
        {
            bool routed = pointers[i].IsInside || pointers[i].IsCaptured;
            if (pointers[i].IsDown && routed)
                ActivePointerCount++;
            if (pointers[i].WasPressed && routed) UserInteractionStarted = true;
        }
        Plugins.Update(in input, deltaTime);
        DetectExternalCameraChange();
    }

    public ViewportSnapshot CaptureSnapshot() => new(
        TryGetVisibleWorldBounds(),
        Center,
        Camera.Zoom,
        Camera.ViewportSize,
        Revision);

    public void MoveCenter(Vector2 center) =>
        MoveCenter(center, ViewportChangeKind.Programmatic);

    public void MoveCorner(Vector2 topLeft)
    {
        ValidateVector(topLeft, nameof(topLeft));
        Bounds2D visible = TryGetVisibleWorldBounds();
        MoveByWorld(topLeft - new Vector2(visible.Left, visible.Top),
            ViewportChangeKind.Programmatic);
    }

    public void MoveByWorld(Vector2 delta) =>
        MoveByWorld(delta, ViewportChangeKind.Programmatic);

    public void SetZoom(float zoom) =>
        SetZoomAt(zoom, Camera.ViewportSize * 0.5f, ViewportChangeKind.Programmatic);

    public void SetZoomAt(float zoom, Vector2 viewportAnchor) =>
        SetZoomAt(zoom, viewportAnchor, ViewportChangeKind.Programmatic);

    public void FitWidth(float worldWidth, bool center = true)
    {
        ValidatePositive(worldWidth, nameof(worldWidth));
        Vector2 previousCenter = Center;
        SetZoom(Camera.ViewportSize.X / worldWidth);
        if (center) MoveCenter(previousCenter);
    }

    public void FitHeight(float worldHeight, bool center = true)
    {
        ValidatePositive(worldHeight, nameof(worldHeight));
        Vector2 previousCenter = Center;
        SetZoom(Camera.ViewportSize.Y / worldHeight);
        if (center) MoveCenter(previousCenter);
    }

    public void FitWorld(Bounds2D worldBounds)
    {
        if (worldBounds.Width <= 0f || worldBounds.Height <= 0f)
            throw new ArgumentException("World bounds must have positive area.", nameof(worldBounds));
        float zoom = MathF.Min(
            Camera.ViewportSize.X / worldBounds.Width,
            Camera.ViewportSize.Y / worldBounds.Height);
        SetZoom(zoom);
        MoveCenter(ToNumerics(worldBounds.Center));
    }

    /// <summary>Call after the bound Camera's viewport size changes.</summary>
    public void Resize()
    {
        ValidateCamera(Camera);
        DetectExternalCameraChange(ViewportChangeKind.Resize);
        Plugins.Resize();
        DetectExternalCameraChange();
    }

    public void ResetPlugins() => Plugins.Reset();

    internal void MarkUserInteractionStarted() => UserInteractionStarted = true;

    internal void PanByScreenDelta(Vector2 screenDelta, ViewportChangeKind kind)
    {
        if (screenDelta == Vector2.Zero) return;
        if (!Camera.TryViewportToWorld(Vector2.Zero, out Vector2 origin) ||
            !Camera.TryViewportToWorld(screenDelta, out Vector2 target))
        {
            return;
        }
        MoveByWorld(origin - target, kind);
    }

    internal void MoveCenter(Vector2 center, ViewportChangeKind kind)
    {
        ValidateVector(center, nameof(center));
        MoveByWorld(center - Center, kind);
    }

    internal void MoveByWorld(Vector2 delta, ViewportChangeKind kind)
    {
        ValidateVector(delta, nameof(delta));
        if (delta == Vector2.Zero) return;
        Vector2 previousCenter = Center;
        float previousZoom = Camera.Zoom;
        Camera.Position += delta;
        MarkChanged(kind, previousCenter, previousZoom);
    }

    internal void SetZoomAt(float zoom, Vector2 viewportAnchor, ViewportChangeKind kind)
    {
        ValidatePositive(zoom, nameof(zoom));
        ValidateVector(viewportAnchor, nameof(viewportAnchor));
        if (Camera.Zoom == zoom) return;
        if (!Camera.TryViewportToWorld(viewportAnchor, out Vector2 before)) return;
        Vector2 previousCenter = Center;
        float previousZoom = Camera.Zoom;
        Camera.Zoom = zoom;
        if (!Camera.TryViewportToWorld(viewportAnchor, out Vector2 after))
        {
            Camera.Zoom = previousZoom;
            return;
        }
        Camera.Position += before - after;
        ZoomAnchor = viewportAnchor;
        MarkChanged(kind, previousCenter, previousZoom);
    }

    internal void SynchronizeWheelTarget(float zoom) =>
        Plugins.Get<ViewportWheelPlugin>(ViewportPluginKeys.Wheel)?.SynchronizeTarget(zoom);

    private Bounds2D TryGetVisibleWorldBounds()
    {
        if (!Camera.TryGetStableVisibleWorldBounds(out Bounds2D bounds))
            throw new InvalidOperationException("Viewport Camera does not produce finite visible bounds.");
        return bounds;
    }

    private void DetectExternalCameraChange(
        ViewportChangeKind kind = ViewportChangeKind.Programmatic)
    {
        if (_observedPosition == Camera.Position &&
            _observedZoom == Camera.Zoom &&
            _observedViewportSize == Camera.ViewportSize)
        {
            return;
        }
        ValidateCamera(Camera);
        MarkChanged(kind, _observedCenter, _observedZoom);
    }

    private void MarkChanged(
        ViewportChangeKind kind,
        Vector2 previousCenter,
        float previousZoom)
    {
        if (Revision == ulong.MaxValue)
            throw new InvalidOperationException("Viewport revision overflowed.");
        Revision++;
        ObserveCamera();
        Changed?.Invoke(new ViewportChangedEvent(
            kind,
            previousCenter,
            Center,
            previousZoom,
            Camera.Zoom,
            Revision));
    }

    private void ObserveCamera()
    {
        _observedPosition = Camera.Position;
        _observedZoom = Camera.Zoom;
        _observedViewportSize = Camera.ViewportSize;
        _observedCenter = Center;
    }

    private static void ValidateCamera(Camera2D camera)
    {
        ValidatePositive(camera.ViewportSize.X, nameof(camera.ViewportSize));
        ValidatePositive(camera.ViewportSize.Y, nameof(camera.ViewportSize));
        ValidatePositive(camera.Zoom, nameof(camera.Zoom));
        ValidateVector(camera.Position, nameof(camera.Position));
    }

    private static void ValidateVector(Vector2 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidatePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(name);
    }

    private static Vector2 ToNumerics(GameEngine.Core.Domain.ValueObjects.Vector2D value) =>
        new((float)value.X, (float)value.Y);
}
