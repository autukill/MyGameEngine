namespace GameEngine.Features.ViewportNavigation;

using GameEngine.Features.Camera.Domain;

public sealed record ViewportNavigationConfiguration
{
    public ViewportDragOptions? Drag { get; }
    public ViewportPinchOptions? Pinch { get; }
    public ViewportWheelOptions? Wheel { get; }
    public ViewportDecelerateOptions? Decelerate { get; }
    public ViewportMouseEdgesOptions? MouseEdges { get; }
    public ViewportAnimateOptions? Animate { get; }
    public ViewportBounceOptions? Bounce { get; }
    public ViewportSnapZoomOptions? SnapZoom { get; }
    public ViewportClampZoomOptions? ClampZoom { get; }
    public ViewportSnapOptions? Snap { get; }
    public ViewportClampOptions? Clamp { get; }

    internal ViewportNavigationConfiguration(
        ViewportDragOptions? drag,
        ViewportPinchOptions? pinch,
        ViewportWheelOptions? wheel,
        ViewportDecelerateOptions? decelerate,
        ViewportMouseEdgesOptions? mouseEdges,
        ViewportAnimateOptions? animate,
        ViewportBounceOptions? bounce,
        ViewportSnapZoomOptions? snapZoom,
        ViewportClampZoomOptions? clampZoom,
        ViewportSnapOptions? snap,
        ViewportClampOptions? clamp)
    {
        Drag = drag;
        Pinch = pinch;
        Wheel = wheel;
        Decelerate = decelerate;
        MouseEdges = mouseEdges;
        Animate = animate;
        Bounce = bounce;
        SnapZoom = snapZoom;
        ClampZoom = clampZoom;
        Snap = snap;
        Clamp = clamp;
    }

    public ViewportController CreateController(Camera2D camera)
    {
        var controller = new ViewportController(camera);
        if (Drag is { } drag) controller.Plugins.Add(new ViewportDragPlugin(drag));
        if (Pinch is { } pinch) controller.Plugins.Add(new ViewportPinchPlugin(pinch));
        if (Wheel is { } wheel) controller.Plugins.Add(new ViewportWheelPlugin(wheel));
        if (MouseEdges is { } mouseEdges)
            controller.Plugins.Add(new ViewportMouseEdgesPlugin(mouseEdges));
        if (Decelerate is { } decelerate)
            controller.Plugins.Add(new ViewportDeceleratePlugin(decelerate));
        if (Animate is { } animate) controller.Plugins.Add(new ViewportAnimatePlugin(animate));
        if (Bounce is { } bounce) controller.Plugins.Add(new ViewportBouncePlugin(bounce));
        if (SnapZoom is { } snapZoom)
            controller.Plugins.Add(new ViewportSnapZoomPlugin(snapZoom));
        if (ClampZoom is { } clampZoom)
            controller.Plugins.Add(new ViewportClampZoomPlugin(clampZoom));
        if (Snap is { } snap) controller.Plugins.Add(new ViewportSnapPlugin(snap));
        if (Clamp is { } clamp) controller.Plugins.Add(new ViewportClampPlugin(clamp));
        return controller;
    }
}

/// <summary>Declarative, allocation-at-assembly-time configuration for one interactive View.</summary>
public sealed class ViewportNavigationBuilder
{
    private ViewportDragOptions? _drag;
    private ViewportPinchOptions? _pinch;
    private ViewportWheelOptions? _wheel;
    private ViewportDecelerateOptions? _decelerate;
    private ViewportMouseEdgesOptions? _mouseEdges;
    private ViewportAnimateOptions? _animate;
    private ViewportBounceOptions? _bounce;
    private ViewportSnapZoomOptions? _snapZoom;
    private ViewportClampZoomOptions? _clampZoom;
    private ViewportSnapOptions? _snap;
    private ViewportClampOptions? _clamp;

    public ViewportNavigationBuilder Drag(ViewportDragOptions? options = null)
    {
        _drag = options ?? ViewportDragOptions.Default;
        return this;
    }

    public ViewportNavigationBuilder Wheel(ViewportWheelOptions? options = null)
    {
        _wheel = options ?? ViewportWheelOptions.Default;
        return this;
    }

    public ViewportNavigationBuilder Pinch(ViewportPinchOptions? options = null)
    {
        _pinch = options ?? ViewportPinchOptions.Default;
        return this;
    }

    public ViewportNavigationBuilder Decelerate(ViewportDecelerateOptions? options = null)
    {
        _decelerate = options ?? ViewportDecelerateOptions.Default;
        return this;
    }

    public ViewportNavigationBuilder MouseEdges(ViewportMouseEdgesOptions? options = null)
    {
        ViewportMouseEdgesOptions value = options ?? ViewportMouseEdgesOptions.Default;
        value.Validate();
        _mouseEdges = value;
        return this;
    }

    public ViewportNavigationBuilder Animate(ViewportAnimateOptions options)
    {
        options.Validate();
        _animate = options;
        return this;
    }

    public ViewportNavigationBuilder Bounce(ViewportBounceOptions options)
    {
        options.Validate();
        _bounce = options;
        return this;
    }

    public ViewportNavigationBuilder SnapZoom(ViewportSnapZoomOptions options)
    {
        options.Validate();
        _snapZoom = options;
        return this;
    }

    public ViewportNavigationBuilder ClampZoom(ViewportClampZoomOptions options)
    {
        _clampZoom = options;
        return this;
    }

    public ViewportNavigationBuilder Clamp(ViewportClampOptions options)
    {
        _clamp = options;
        return this;
    }

    public ViewportNavigationBuilder Snap(ViewportSnapOptions options)
    {
        options.Validate();
        _snap = options;
        return this;
    }

    public ViewportNavigationConfiguration Build()
    {
        if (_drag is null && _pinch is null && _wheel is null && _decelerate is null &&
            _mouseEdges is null && _animate is null && _bounce is null && _snapZoom is null &&
            _clampZoom is null && _snap is null && _clamp is null)
        {
            throw new InvalidOperationException("At least one Viewport navigation plugin is required.");
        }
        if (_bounce is not null && _clamp is not null)
            throw new InvalidOperationException("Bounce and hard Clamp cannot own the same bounds.");
        if (_animate is { ChangesPosition: true } && _snap is not null)
            throw new InvalidOperationException("Animate position and Snap cannot own position together.");
        if (_animate is { ChangesZoom: true } && _snapZoom is not null)
            throw new InvalidOperationException("Animate zoom and SnapZoom cannot own zoom together.");
        return new ViewportNavigationConfiguration(
            _drag,
            _pinch,
            _wheel,
            _decelerate,
            _mouseEdges,
            _animate,
            _bounce,
            _snapZoom,
            _clampZoom,
            _snap,
            _clamp);
    }
}
