namespace GameEngine.Features.ViewportNavigation;

using GameEngine.Features.Camera.Domain;

public sealed record ViewportNavigationConfiguration
{
    public ViewportDragOptions? Drag { get; }
    public ViewportPinchOptions? Pinch { get; }
    public ViewportWheelOptions? Wheel { get; }
    public ViewportDecelerateOptions? Decelerate { get; }
    public ViewportClampZoomOptions? ClampZoom { get; }
    public ViewportClampOptions? Clamp { get; }

    internal ViewportNavigationConfiguration(
        ViewportDragOptions? drag,
        ViewportPinchOptions? pinch,
        ViewportWheelOptions? wheel,
        ViewportDecelerateOptions? decelerate,
        ViewportClampZoomOptions? clampZoom,
        ViewportClampOptions? clamp)
    {
        Drag = drag;
        Pinch = pinch;
        Wheel = wheel;
        Decelerate = decelerate;
        ClampZoom = clampZoom;
        Clamp = clamp;
    }

    public ViewportController CreateController(Camera2D camera)
    {
        var controller = new ViewportController(camera);
        if (Drag is { } drag) controller.Plugins.Add(new ViewportDragPlugin(drag));
        if (Pinch is { } pinch) controller.Plugins.Add(new ViewportPinchPlugin(pinch));
        if (Wheel is { } wheel) controller.Plugins.Add(new ViewportWheelPlugin(wheel));
        if (Decelerate is { } decelerate)
            controller.Plugins.Add(new ViewportDeceleratePlugin(decelerate));
        if (ClampZoom is { } clampZoom)
            controller.Plugins.Add(new ViewportClampZoomPlugin(clampZoom));
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
    private ViewportClampZoomOptions? _clampZoom;
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

    public ViewportNavigationConfiguration Build()
    {
        if (_drag is null && _pinch is null && _wheel is null && _decelerate is null &&
            _clampZoom is null && _clamp is null)
        {
            throw new InvalidOperationException("At least one Viewport navigation plugin is required.");
        }
        return new ViewportNavigationConfiguration(
            _drag, _pinch, _wheel, _decelerate, _clampZoom, _clamp);
    }
}
