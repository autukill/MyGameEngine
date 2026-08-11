namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;

public sealed class ViewportMouseEdgesPlugin : ViewportPlugin
{
    private readonly ViewportMouseEdgesOptions _options;
    private Vector2 _lastWorldVelocity;

    public ViewportMouseEdgesOptions Options => _options;
    public bool IsActive { get; private set; }

    public ViewportMouseEdgesPlugin(ViewportMouseEdgesOptions options)
        : base(ViewportPluginKeys.MouseEdges, ViewportPluginOrders.MouseEdges)
    {
        options.Validate();
        _options = options;
    }

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (deltaTime <= 0d)
        {
            Stop(controller, transferVelocity: false);
            return;
        }
        if (!TryGetMouse(in input, out ViewportPointer mouse) || !mouse.IsInside)
        {
            Stop(controller, transferVelocity: false);
            return;
        }
        if (!CanActivate(mouse, controller.ActivePointerCount, _options.Activation))
        {
            Stop(controller, transferVelocity: false);
            return;
        }

        Vector2 direction = _options.Radius is { } radius
            ? RadiusDirection(mouse.Position, controller.ScreenSize, radius, _options.LinearRadius)
            : EdgeDirection(mouse.Position, controller.ScreenSize, _options.Insets!.Value);
        if (_options.Reverse) direction = -direction;
        if (direction == Vector2.Zero)
        {
            Stop(controller, transferVelocity: true);
            return;
        }

        ViewportDeceleratePlugin? decelerate = controller.Plugins
            .Get<ViewportDeceleratePlugin>(ViewportPluginKeys.Decelerate);
        if (!IsActive && !_options.InterruptDeceleration &&
            decelerate?.IsActive == true)
        {
            return;
        }

        controller.MarkUserInteractionStarted();
        Vector2 before = controller.Position;
        Vector2 screenDelta = -direction * _options.SpeedPixelsPerSecond * (float)deltaTime;
        controller.PanByScreenDelta(screenDelta, ViewportChangeKind.MouseEdges);
        _lastWorldVelocity = (controller.Position - before) / (float)deltaTime;
        IsActive = true;
    }

    protected override void OnReset(ViewportController controller)
    {
        IsActive = false;
        _lastWorldVelocity = Vector2.Zero;
    }

    private void Stop(ViewportController controller, bool transferVelocity)
    {
        if (!IsActive) return;
        if (transferVelocity && _options.UseDeceleration &&
            _lastWorldVelocity != Vector2.Zero)
        {
            controller.Plugins
                .Get<ViewportDeceleratePlugin>(ViewportPluginKeys.Decelerate)?
                .Activate(_lastWorldVelocity);
        }
        IsActive = false;
        _lastWorldVelocity = Vector2.Zero;
    }

    private static bool TryGetMouse(
        in ViewportInputFrame input,
        out ViewportPointer mouse)
    {
        ReadOnlySpan<ViewportPointer> pointers = input.Pointers;
        for (int i = 0; i < pointers.Length; i++)
        {
            if (pointers[i].Kind != PointerKind.Mouse) continue;
            mouse = pointers[i];
            return true;
        }
        mouse = default;
        return false;
    }

    private static bool CanActivate(
        ViewportPointer mouse,
        int activePointerCount,
        ViewportMouseEdgesActivation activation) => activation switch
    {
        ViewportMouseEdgesActivation.PointerDown => mouse.IsDown,
        ViewportMouseEdgesActivation.Hover => !mouse.IsDown && activePointerCount == 0,
        ViewportMouseEdgesActivation.Always => mouse.IsDown || activePointerCount == 0,
        _ => false,
    };

    private static Vector2 EdgeDirection(
        Vector2 position,
        Vector2 screenSize,
        ViewportEdgeInsets insets)
    {
        float x = 0f;
        float y = 0f;
        if (insets.Left is { } left && position.X <= left) x = -1f;
        else if (insets.Right is { } right && position.X >= screenSize.X - right) x = 1f;
        if (insets.Top is { } top && position.Y <= top) y = -1f;
        else if (insets.Bottom is { } bottom && position.Y >= screenSize.Y - bottom) y = 1f;
        var direction = new Vector2(x, y);
        return direction == Vector2.Zero ? direction : Vector2.Normalize(direction);
    }

    private static Vector2 RadiusDirection(
        Vector2 position,
        Vector2 screenSize,
        float radius,
        bool linear)
    {
        Vector2 delta = position - screenSize * 0.5f;
        if (delta.LengthSquared() < radius * radius) return Vector2.Zero;
        if (linear)
            return new Vector2(MathF.Sign(delta.X), MathF.Sign(delta.Y));
        return delta == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(delta);
    }
}

public sealed class ViewportAnimatePlugin : ViewportPlugin
{
    private readonly ViewportAnimateOptions _options;
    private Vector2 _startCenter;
    private Vector2 _targetCenter;
    private float _startZoom;
    private float _targetZoom;
    private double _elapsed;
    private bool _restartAfterInteraction;

    public ViewportAnimateOptions Options => _options;
    public ViewportMotionState State { get; private set; } = ViewportMotionState.Idle;

    public ViewportAnimatePlugin(ViewportAnimateOptions options)
        : base(ViewportPluginKeys.Animate, ViewportPluginOrders.Animate)
    {
        options.Validate();
        _options = options;
    }

    public void Restart() => Begin(Controller);

    protected override void OnAttached(ViewportController controller) => Begin(controller);

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (State is ViewportMotionState.Completed or ViewportMotionState.Cancelled) return;
        if (ViewportMotionRuntime.IsInteracting(controller))
        {
            if (_options.InterruptMode == ViewportMotionInterruptMode.Cancel)
                State = ViewportMotionState.Cancelled;
            else if (_options.InterruptMode == ViewportMotionInterruptMode.Pause)
                _restartAfterInteraction = true;
            if (_options.InterruptMode != ViewportMotionInterruptMode.Ignore) return;
        }
        if (_restartAfterInteraction)
        {
            Begin(controller);
            _restartAfterInteraction = false;
        }
        _elapsed = Math.Min(_options.DurationSeconds, _elapsed + deltaTime);
        float t = Easing.Evaluate(_options.Easing, _elapsed / _options.DurationSeconds);
        Apply(controller, t);
        if (_elapsed >= _options.DurationSeconds)
        {
            Apply(controller, 1f);
            State = ViewportMotionState.Completed;
        }
    }

    protected override void OnReset(ViewportController controller) => Begin(controller);

    private void Begin(ViewportController controller)
    {
        _startCenter = controller.Center;
        _targetCenter = _options.Center ?? _startCenter;
        _startZoom = controller.Zoom;
        _targetZoom = _options.ChangesZoom ? _options.ResolveZoom(controller) : _startZoom;
        _elapsed = 0d;
        _restartAfterInteraction = false;
        State = ViewportMotionState.Running;
    }

    private void Apply(ViewportController controller, float progress)
    {
        if (_options.ChangesZoom)
        {
            float zoom = _startZoom + (_targetZoom - _startZoom) * progress;
            controller.SetZoomAt(
                zoom,
                controller.ScreenSize * 0.5f,
                ViewportChangeKind.Animate);
        }
        if (_options.ChangesPosition)
            controller.MoveCenter(
                Vector2.Lerp(_startCenter, _targetCenter, progress),
                ViewportChangeKind.Animate);
    }
}

public sealed class ViewportBouncePlugin : ViewportPlugin
{
    private readonly ViewportBounceOptions _options;
    private Vector2 _startCenter;
    private Vector2 _targetCenter;
    private double _elapsed;

    public ViewportBounceOptions Options => _options;
    public ViewportMotionState State { get; private set; }

    public ViewportBouncePlugin(ViewportBounceOptions options)
        : base(ViewportPluginKeys.Bounce, ViewportPluginOrders.Bounce)
    {
        options.Validate();
        _options = options;
    }

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (controller.ActivePointerCount > 0 || controller.DragActive)
        {
            State = ViewportMotionState.Idle;
            return;
        }
        if (State != ViewportMotionState.Running && !TryBegin(controller)) return;
        _elapsed = Math.Min(_options.DurationSeconds, _elapsed + deltaTime);
        float t = Easing.Evaluate(_options.Easing, _elapsed / _options.DurationSeconds);
        controller.MoveCenter(
            Vector2.Lerp(_startCenter, _targetCenter, t),
            ViewportChangeKind.Bounce);
        if (_elapsed >= _options.DurationSeconds)
        {
            controller.MoveCenter(_targetCenter, ViewportChangeKind.Bounce);
            State = ViewportMotionState.Completed;
        }
    }

    protected override void OnResize(ViewportController controller) => State = ViewportMotionState.Idle;
    protected override void OnReset(ViewportController controller) => State = ViewportMotionState.Idle;

    private bool TryBegin(ViewportController controller)
    {
        Vector2 correction = ViewportMotionRuntime.ResolveBoundsCorrection(
            controller.VisibleWorldBounds,
            _options.WorldBounds,
            _options.Axis,
            _options.Underflow);
        if (correction == Vector2.Zero)
        {
            State = ViewportMotionState.Completed;
            return false;
        }
        _startCenter = controller.Center;
        _targetCenter = _startCenter + correction;
        _elapsed = 0d;
        State = ViewportMotionState.Running;
        ViewportDeceleratePlugin? decelerate =
            controller.Plugins.Get<ViewportDeceleratePlugin>(ViewportPluginKeys.Decelerate);
        if (correction.X != 0f) decelerate?.StopHorizontal();
        if (correction.Y != 0f) decelerate?.StopVertical();
        return true;
    }
}

public sealed class ViewportSnapZoomPlugin : ViewportPlugin
{
    private readonly ViewportSnapZoomOptions _options;
    private float _startZoom;
    private float _targetZoom;
    private double _elapsed;
    private bool _needsRestart = true;
    private ViewportMotionState _state = ViewportMotionState.Idle;

    public ViewportSnapZoomOptions Options => _options;
    public ViewportMotionState State => _state;

    public ViewportSnapZoomPlugin(ViewportSnapZoomOptions options)
        : base(ViewportPluginKeys.SnapZoom, ViewportPluginOrders.SnapZoom)
    {
        options.Validate();
        _options = options;
    }

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (!ViewportMotionRuntime.HandleReactiveInterruption(
                controller, _options.InterruptMode, ref _state, ref _needsRestart))
            return;
        _targetZoom = _options.ResolveZoom(controller);
        if (!_needsRestart && _state == ViewportMotionState.Completed &&
            MathF.Abs(controller.Zoom - _targetZoom) <= 0.0001f)
        {
            return;
        }
        if (_needsRestart || _state != ViewportMotionState.Running) Begin(controller);
        _elapsed = Math.Min(_options.DurationSeconds, _elapsed + deltaTime);
        float t = Easing.Evaluate(_options.Easing, _elapsed / _options.DurationSeconds);
        float zoom = _startZoom + (_targetZoom - _startZoom) * t;
        controller.SetZoomAt(
            zoom,
            _options.ViewportAnchor ?? controller.ScreenSize * 0.5f,
            ViewportChangeKind.SnapZoom);
        if (_elapsed >= _options.DurationSeconds)
        {
            controller.SetZoomAt(
                _targetZoom,
                _options.ViewportAnchor ?? controller.ScreenSize * 0.5f,
                ViewportChangeKind.SnapZoom);
            _state = ViewportMotionState.Completed;
        }
    }

    protected override void OnResize(ViewportController controller) => _needsRestart = true;
    protected override void OnReset(ViewportController controller)
    {
        _state = ViewportMotionState.Idle;
        _needsRestart = true;
    }

    private void Begin(ViewportController controller)
    {
        _startZoom = controller.Zoom;
        _targetZoom = _options.ResolveZoom(controller);
        _elapsed = 0d;
        _needsRestart = false;
        _state = ViewportMotionState.Running;
    }
}

public sealed class ViewportSnapPlugin : ViewportPlugin
{
    private readonly ViewportSnapOptions _options;
    private Vector2 _start;
    private double _elapsed;
    private bool _needsRestart = true;
    private ViewportMotionState _state = ViewportMotionState.Idle;

    public ViewportSnapOptions Options => _options;
    public ViewportMotionState State => _state;

    public ViewportSnapPlugin(ViewportSnapOptions options)
        : base(ViewportPluginKeys.Snap, ViewportPluginOrders.Snap)
    {
        options.Validate();
        _options = options;
    }

    protected override void OnUpdate(
        ViewportController controller,
        in ViewportInputFrame input,
        double deltaTime)
    {
        if (!ViewportMotionRuntime.HandleReactiveInterruption(
                controller, _options.InterruptMode, ref _state, ref _needsRestart))
            return;
        Vector2 current = Current(controller);
        if (!_needsRestart && _state == ViewportMotionState.Completed &&
            Vector2.DistanceSquared(current, _options.Target) <= 0.000001f)
        {
            return;
        }
        if (_needsRestart || _state != ViewportMotionState.Running) Begin(current);
        _elapsed = Math.Min(_options.DurationSeconds, _elapsed + deltaTime);
        float t = Easing.Evaluate(_options.Easing, _elapsed / _options.DurationSeconds);
        Apply(controller, Vector2.Lerp(_start, _options.Target, t));
        if (_elapsed >= _options.DurationSeconds)
        {
            Apply(controller, _options.Target);
            _state = ViewportMotionState.Completed;
        }
    }

    protected override void OnResize(ViewportController controller) => _needsRestart = true;
    protected override void OnReset(ViewportController controller)
    {
        _state = ViewportMotionState.Idle;
        _needsRestart = true;
    }

    private Vector2 Current(ViewportController controller) => _options.UseTopLeft
        ? new Vector2(controller.VisibleWorldBounds.Left, controller.VisibleWorldBounds.Top)
        : controller.Center;

    private void Begin(Vector2 current)
    {
        _start = current;
        _elapsed = 0d;
        _needsRestart = false;
        _state = ViewportMotionState.Running;
    }

    private void Apply(ViewportController controller, Vector2 value)
    {
        if (_options.UseTopLeft)
        {
            Vector2 current = Current(controller);
            controller.MoveByWorld(value - current, ViewportChangeKind.Snap);
        }
        else
        {
            controller.MoveCenter(value, ViewportChangeKind.Snap);
        }
    }
}

internal static class ViewportMotionRuntime
{
    public static bool IsInteracting(ViewportController controller) =>
        controller.UserInteractionStarted || controller.DragActive || controller.ActivePointerCount >= 2;

    public static bool HandleReactiveInterruption(
        ViewportController controller,
        ViewportMotionInterruptMode mode,
        ref ViewportMotionState state,
        ref bool needsRestart)
    {
        if (!IsInteracting(controller)) return state != ViewportMotionState.Cancelled;
        if (mode == ViewportMotionInterruptMode.Cancel)
            state = ViewportMotionState.Cancelled;
        else if (mode == ViewportMotionInterruptMode.Pause)
            needsRestart = true;
        return mode == ViewportMotionInterruptMode.Ignore;
    }

    public static Vector2 ResolveBoundsCorrection(
        Bounds2D view,
        Bounds2D world,
        ViewportAxis axis,
        ViewportUnderflow underflow)
    {
        float x = axis == ViewportAxis.Vertical
            ? 0f
            : ResolveOffset(view.Left, view.Right, world.Left, world.Right,
                HorizontalUnderflow(underflow));
        float y = axis == ViewportAxis.Horizontal
            ? 0f
            : ResolveOffset(view.Top, view.Bottom, world.Top, world.Bottom,
                VerticalUnderflow(underflow));
        return new Vector2(x, y);
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
