namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

public static class ViewportPluginKeys
{
    public const string Drag = "drag";
    public const string Pinch = "pinch";
    public const string Wheel = "wheel";
    public const string Follow = "follow";
    public const string MouseEdges = "mouse-edges";
    public const string Decelerate = "decelerate";
    public const string Animate = "animate";
    public const string Bounce = "bounce";
    public const string SnapZoom = "snap-zoom";
    public const string ClampZoom = "clamp-zoom";
    public const string Snap = "snap";
    public const string Clamp = "clamp";
}

internal static class ViewportPluginOrders
{
    public const int Drag = 0;
    public const int Pinch = 100;
    public const int Wheel = 200;
    public const int Follow = 300;
    public const int MouseEdges = 400;
    public const int Decelerate = 500;
    public const int Animate = 600;
    public const int Bounce = 700;
    public const int SnapZoom = 800;
    public const int ClampZoom = 900;
    public const int Snap = 1000;
    public const int Clamp = 1100;
}

public sealed class ViewportDragPlugin : ViewportPlugin
{
    private readonly ViewportDragOptions _options;
    private Vector2 _pressPosition;
    private Vector2 _lastPosition;
    private Vector2 _velocity;
    private bool _previousDown;
    private bool _captured;
    private bool _moved;

    public ViewportDragOptions Options => _options;
    public bool IsDragging => _captured && _previousDown;

    public ViewportDragPlugin(ViewportDragOptions options)
        : base(ViewportPluginKeys.Drag, ViewportPluginOrders.Drag) => _options = options;

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        bool pressed = input.PrimaryDown && !_previousDown;
        bool released = !input.PrimaryDown && _previousDown;
        if (pressed && input.IsPointerInside)
        {
            _captured = true;
            _moved = false;
            _pressPosition = input.PointerPosition;
            _lastPosition = input.PointerPosition;
            _velocity = Vector2.Zero;
            controller.MarkUserInteractionStarted();
        }

        if (_captured && input.PrimaryDown)
        {
            controller.DragActive = true;
            Vector2 delta = input.PointerPosition - _lastPosition;
            _lastPosition = input.PointerPosition;
            if (!_moved && Vector2.DistanceSquared(input.PointerPosition, _pressPosition) >=
                _options.ThresholdPixels * _options.ThresholdPixels)
            {
                _moved = true;
            }
            if (_moved && delta != Vector2.Zero)
            {
                delta = Filter(delta, _options.Axis);
                Vector2 before = controller.Position;
                controller.PanByScreenDelta(delta, ViewportChangeKind.Drag);
                if (deltaTime > 0d)
                    _velocity = (controller.Position - before) / (float)deltaTime;
            }
        }

        if (_captured && released)
        {
            _captured = false;
            controller.DragReleased = _moved;
            controller.ReleasedVelocity = _moved ? _velocity : Vector2.Zero;
        }
        _previousDown = input.PrimaryDown;
    }

    protected override void OnReset(ViewportController controller)
    {
        _previousDown = false;
        _captured = false;
        _moved = false;
        _velocity = Vector2.Zero;
    }

    private static Vector2 Filter(Vector2 value, ViewportAxis axis) => axis switch
    {
        ViewportAxis.Horizontal => new Vector2(value.X, 0f),
        ViewportAxis.Vertical => new Vector2(0f, value.Y),
        _ => value,
    };
}

public sealed class ViewportWheelPlugin : ViewportPlugin
{
    private readonly ViewportWheelOptions _options;
    private Vector2 _anchor;
    private float _targetZoom;
    private int _remainingFrames;
    private bool _previousDown;

    public ViewportWheelOptions Options => _options;
    public bool IsSmoothing => _remainingFrames > 0;

    public ViewportWheelPlugin(ViewportWheelOptions options)
        : base(ViewportPluginKeys.Wheel, ViewportPluginOrders.Wheel) => _options = options;

    protected override void OnAttached(ViewportController controller) =>
        _targetZoom = controller.Zoom;

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        bool pressed = input.PrimaryDown && !_previousDown;
        if (pressed && _options.InterruptOnPointerDown) SynchronizeTarget(controller.Zoom);

        if (input.IsPointerInside && input.ScrollDelta != 0f)
        {
            controller.MarkUserInteractionStarted();
            _anchor = input.PointerPosition;
            float direction = _options.Reverse ? -1f : 1f;
            float basis = 1f + _options.Percent;
            float start = _remainingFrames > 0 ? _targetZoom : controller.Zoom;
            _targetZoom = start * MathF.Pow(basis, input.ScrollDelta * direction);
            if (!float.IsFinite(_targetZoom) || _targetZoom <= 0f)
                _targetZoom = controller.Zoom;
            _remainingFrames = _options.SmoothFrames;
            if (_remainingFrames == 0)
                controller.SetZoomAt(_targetZoom, _anchor, ViewportChangeKind.Wheel);
        }

        if (_remainingFrames > 0)
        {
            float next = controller.Zoom +
                (_targetZoom - controller.Zoom) / _remainingFrames;
            _remainingFrames--;
            controller.SetZoomAt(next, _anchor, ViewportChangeKind.Wheel);
            if (_remainingFrames == 0) _targetZoom = controller.Zoom;
        }
        _previousDown = input.PrimaryDown;
    }

    protected override void OnReset(ViewportController controller)
    {
        _previousDown = false;
        SynchronizeTarget(controller.Zoom);
    }

    internal void SynchronizeTarget(float zoom)
    {
        _targetZoom = zoom;
        _remainingFrames = 0;
    }
}

public sealed class ViewportDeceleratePlugin : ViewportPlugin
{
    private readonly ViewportDecelerateOptions _options;
    private Vector2 _velocity;

    public ViewportDecelerateOptions Options => _options;
    public Vector2 Velocity => _velocity;
    public bool IsActive => _velocity != Vector2.Zero;

    public ViewportDeceleratePlugin(ViewportDecelerateOptions options)
        : base(ViewportPluginKeys.Decelerate, ViewportPluginOrders.Decelerate) => _options = options;

    public void Activate(Vector2 velocity)
    {
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            throw new ArgumentOutOfRangeException(nameof(velocity));
        _velocity = velocity;
    }

    public void StopHorizontal() => _velocity.X = 0f;
    public void StopVertical() => _velocity.Y = 0f;

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (controller.UserInteractionStarted) _velocity = Vector2.Zero;
        if (controller.DragReleased) _velocity = controller.ReleasedVelocity;
        if (controller.DragActive || _velocity == Vector2.Zero || deltaTime <= 0d) return;

        float dt = (float)deltaTime;
        float decayRate = 60f * MathF.Log(_options.Friction);
        float decay = MathF.Exp(decayRate * dt);
        Vector2 displacement = _velocity * ((decay - 1f) / decayRate);
        controller.MoveByWorld(displacement, ViewportChangeKind.Decelerate);
        _velocity *= decay;
        if (MathF.Abs(_velocity.X) < _options.MinimumSpeed) _velocity.X = 0f;
        if (MathF.Abs(_velocity.Y) < _options.MinimumSpeed) _velocity.Y = 0f;
    }

    protected override void OnReset(ViewportController controller) => _velocity = Vector2.Zero;
}

public sealed class ViewportClampZoomPlugin : ViewportPlugin
{
    private readonly ViewportClampZoomOptions _options;

    public ViewportClampZoomOptions Options => _options;

    public ViewportClampZoomPlugin(ViewportClampZoomOptions options)
        : base(ViewportPluginKeys.ClampZoom, ViewportPluginOrders.ClampZoom)
    {
        if (options.MinWidth is null && options.MinHeight is null &&
            options.MaxWidth is null && options.MaxHeight is null &&
            options.MinScale is null && options.MaxScale is null)
        {
            throw new ArgumentException("At least one zoom constraint is required.", nameof(options));
        }
        _options = options;
    }

    protected override void OnAttached(ViewportController controller) => Clamp(controller);

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime) => Clamp(controller);

    protected override void OnResize(ViewportController controller) => Clamp(controller);
    protected override void OnReset(ViewportController controller) => Clamp(controller);

    private void Clamp(ViewportController controller)
    {
        float minimum = _options.MinScale ?? 0f;
        float maximum = _options.MaxScale ?? float.PositiveInfinity;
        if (_options.MaxWidth is { } maxWidth)
            minimum = MathF.Max(minimum, controller.ScreenSize.X / maxWidth);
        if (_options.MaxHeight is { } maxHeight)
            minimum = MathF.Max(minimum, controller.ScreenSize.Y / maxHeight);
        if (_options.MinWidth is { } minWidth)
            maximum = MathF.Min(maximum, controller.ScreenSize.X / minWidth);
        if (_options.MinHeight is { } minHeight)
            maximum = MathF.Min(maximum, controller.ScreenSize.Y / minHeight);
        if (minimum > maximum)
            throw new InvalidOperationException(
                "Viewport zoom constraints cannot be satisfied for the current screen size.");
        float clamped = Math.Clamp(controller.Zoom, minimum, maximum);
        if (clamped == controller.Zoom) return;
        Vector2 anchor = controller.ZoomAnchor ?? controller.ScreenSize * 0.5f;
        controller.SetZoomAt(clamped, anchor, ViewportChangeKind.ClampZoom);
        controller.SynchronizeWheelTarget(clamped);
    }
}

public sealed class ViewportClampPlugin : ViewportPlugin
{
    private readonly ViewportClampOptions _options;

    public ViewportClampOptions Options => _options;

    public ViewportClampPlugin(ViewportClampOptions options)
        : base(ViewportPluginKeys.Clamp, ViewportPluginOrders.Clamp) => _options = options;

    protected override void OnAttached(ViewportController controller) => Clamp(controller);

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime) => Clamp(controller);

    protected override void OnResize(ViewportController controller) => Clamp(controller);
    protected override void OnReset(ViewportController controller) => Clamp(controller);

    private void Clamp(ViewportController controller)
    {
        Bounds2D view = controller.VisibleWorldBounds;
        Bounds2D world = _options.WorldBounds;
        float x = _options.Axis == ViewportAxis.Vertical
            ? 0f
            : ResolveOffset(view.Left, view.Right, world.Left, world.Right,
                HorizontalUnderflow(_options.Underflow));
        float y = _options.Axis == ViewportAxis.Horizontal
            ? 0f
            : ResolveOffset(view.Top, view.Bottom, world.Top, world.Bottom,
                VerticalUnderflow(_options.Underflow));
        if (x == 0f && y == 0f) return;
        controller.MoveByWorld(new Vector2(x, y), ViewportChangeKind.Clamp);
        ViewportDeceleratePlugin? decelerate =
            controller.Plugins.Get<ViewportDeceleratePlugin>(ViewportPluginKeys.Decelerate);
        if (x != 0f) decelerate?.StopHorizontal();
        if (y != 0f) decelerate?.StopVertical();
    }

    private static float ResolveOffset(
        float viewMinimum,
        float viewMaximum,
        float worldMinimum,
        float worldMaximum,
        int underflow)
    {
        float viewSize = viewMaximum - viewMinimum;
        float worldSize = worldMaximum - worldMinimum;
        if (viewSize >= worldSize)
        {
            return underflow switch
            {
                -1 => worldMinimum - viewMinimum,
                1 => worldMaximum - viewMaximum,
                0 => (worldMinimum + worldMaximum - viewMinimum - viewMaximum) * 0.5f,
                _ => 0f,
            };
        }
        if (viewMinimum < worldMinimum) return worldMinimum - viewMinimum;
        if (viewMaximum > worldMaximum) return worldMaximum - viewMaximum;
        return 0f;
    }

    private static int HorizontalUnderflow(ViewportUnderflow value) => value switch
    {
        ViewportUnderflow.None => 2,
        ViewportUnderflow.TopLeft or ViewportUnderflow.Left or ViewportUnderflow.BottomLeft => -1,
        ViewportUnderflow.TopRight or ViewportUnderflow.Right or ViewportUnderflow.BottomRight => 1,
        _ => 0,
    };

    private static int VerticalUnderflow(ViewportUnderflow value) => value switch
    {
        ViewportUnderflow.None => 2,
        ViewportUnderflow.TopLeft or ViewportUnderflow.Top or ViewportUnderflow.TopRight => -1,
        ViewportUnderflow.BottomLeft or ViewportUnderflow.Bottom or ViewportUnderflow.BottomRight => 1,
        _ => 0,
    };
}
