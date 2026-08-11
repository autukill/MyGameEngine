namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;

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
    private PointerId _pointerId;
    private Vector2 _pressPosition;
    private Vector2 _lastPosition;
    private Vector2 _velocity;
    private bool _captured;
    private bool _moved;
    private bool _resumeAfterPinch;

    public ViewportDragOptions Options => _options;
    public bool IsDragging => _captured;

    public ViewportDragPlugin(ViewportDragOptions options)
        : base(ViewportPluginKeys.Drag, ViewportPluginOrders.Drag) => _options = options;

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (controller.ActivePointerCount >= 2)
        {
            _captured = false;
            _moved = false;
            _velocity = Vector2.Zero;
            _resumeAfterPinch = true;
            return;
        }

        if (_captured)
        {
            if (!input.TryGetPointer(_pointerId, out ViewportPointer pointer) || !pointer.IsDown)
            {
                _captured = false;
                controller.DragReleased = _moved;
                controller.ReleasedVelocity = _moved ? _velocity : Vector2.Zero;
                _moved = false;
                return;
            }
            controller.DragActive = true;
            Vector2 delta = pointer.Position - _lastPosition;
            _lastPosition = pointer.Position;
            if (!_moved && Vector2.DistanceSquared(pointer.Position, _pressPosition) >=
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
            return;
        }

        ReadOnlySpan<ViewportPointer> pointers = input.Pointers;
        for (int i = 0; i < pointers.Length; i++)
        {
            ViewportPointer pointer = pointers[i];
            if (!pointer.IsDown || !pointer.IsInside ||
                (!pointer.WasPressed && !_resumeAfterPinch))
            {
                continue;
            }
            _pointerId = pointer.Id;
            _captured = true;
            _moved = false;
            _resumeAfterPinch = false;
            _pressPosition = pointer.Position;
            _lastPosition = pointer.Position;
            _velocity = Vector2.Zero;
            controller.MarkUserInteractionStarted();
            break;
        }
    }

    protected override void OnReset(ViewportController controller)
    {
        _captured = false;
        _moved = false;
        _resumeAfterPinch = false;
        _velocity = Vector2.Zero;
    }

    private static Vector2 Filter(Vector2 value, ViewportAxis axis) => axis switch
    {
        ViewportAxis.Horizontal => new Vector2(value.X, 0f),
        ViewportAxis.Vertical => new Vector2(0f, value.Y),
        _ => value,
    };
}

public sealed class ViewportPinchPlugin : ViewportPlugin
{
    private readonly ViewportPinchOptions _options;
    private PointerId _firstId;
    private PointerId _secondId;
    private Vector2 _lastCenter;
    private float _lastDistance;

    public ViewportPinchOptions Options => _options;
    public bool IsPinching { get; private set; }

    public ViewportPinchPlugin(ViewportPinchOptions options)
        : base(ViewportPluginKeys.Pinch, ViewportPluginOrders.Pinch) => _options = options;

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (!IsPinching)
        {
            if (!TryFindPair(in input, out ViewportPointer first, out ViewportPointer second))
                return;
            _firstId = first.Id;
            _secondId = second.Id;
            _lastCenter = (first.Position + second.Position) * 0.5f;
            _lastDistance = Vector2.Distance(first.Position, second.Position);
            if (_lastDistance < _options.MinimumDistancePixels) return;
            IsPinching = true;
            controller.MarkUserInteractionStarted();
            return;
        }

        if (!input.TryGetPointer(_firstId, out ViewportPointer currentFirst) ||
            !input.TryGetPointer(_secondId, out ViewportPointer currentSecond) ||
            !currentFirst.IsDown || !currentSecond.IsDown)
        {
            IsPinching = false;
            return;
        }

        Vector2 center = (currentFirst.Position + currentSecond.Position) * 0.5f;
        float distance = Vector2.Distance(currentFirst.Position, currentSecond.Position);
        if (distance < _options.MinimumDistancePixels || _lastDistance <= 0f)
        {
            _lastCenter = center;
            _lastDistance = distance;
            return;
        }

        controller.MarkUserInteractionStarted();
        if (_options.EnablePan)
        {
            Vector2 centerDelta = (center - _lastCenter) * _options.PanFactor;
            controller.PanByScreenDelta(centerDelta, ViewportChangeKind.Pinch);
        }
        float ratio = distance / _lastDistance;
        float zoom = controller.Zoom * MathF.Pow(ratio, _options.ZoomSpeed);
        if (float.IsFinite(zoom) && zoom > 0f && zoom != controller.Zoom)
            controller.SetZoomAt(zoom, center, ViewportChangeKind.Pinch);
        _lastCenter = center;
        _lastDistance = distance;
    }

    protected override void OnReset(ViewportController controller)
    {
        IsPinching = false;
        _lastDistance = 0f;
    }

    private static bool TryFindPair(
        in ViewportInputFrame input,
        out ViewportPointer first,
        out ViewportPointer second)
    {
        first = default;
        second = default;
        bool foundFirst = false;
        ReadOnlySpan<ViewportPointer> pointers = input.Pointers;
        for (int i = 0; i < pointers.Length; i++)
        {
            ViewportPointer pointer = pointers[i];
            if (!pointer.IsDown || (!pointer.IsInside && !pointer.IsCaptured)) continue;
            if (!foundFirst)
            {
                first = pointer;
                foundFirst = true;
            }
            else
            {
                second = pointer;
                return true;
            }
        }
        return false;
    }
}

public sealed class ViewportWheelPlugin : ViewportPlugin
{
    private readonly ViewportWheelOptions _options;
    private Vector2 _anchor;
    private float _targetZoom;
    private int _remainingFrames;

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
        bool pressed = false;
        ReadOnlySpan<ViewportPointer> pointers = input.Pointers;
        for (int i = 0; i < pointers.Length; i++)
            pressed |= pointers[i].WasPressed &&
                (pointers[i].IsInside || pointers[i].IsCaptured);
        if (pressed && _options.InterruptOnPointerDown) SynchronizeTarget(controller.Zoom);

        if (input.IsScrollInside && input.ScrollDelta != 0f)
        {
            controller.MarkUserInteractionStarted();
            _anchor = input.ScrollPosition;
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
    }

    protected override void OnReset(ViewportController controller)
    {
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
