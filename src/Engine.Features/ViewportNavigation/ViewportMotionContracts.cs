namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

public enum ViewportMotionInterruptMode
{
    Pause = 0,
    Cancel = 1,
    Ignore = 2,
}

public enum ViewportMotionState
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
}

public readonly record struct ViewportAnimateOptions
{
    public Vector2? Center { get; }
    public float? Zoom { get; }
    public float? VisibleWidth { get; }
    public float? VisibleHeight { get; }
    public double DurationSeconds { get; }
    public EasingKind Easing { get; }
    public ViewportMotionInterruptMode InterruptMode { get; }

    public bool ChangesPosition => Center is not null;
    public bool ChangesZoom => Zoom is not null || VisibleWidth is not null || VisibleHeight is not null;

    public ViewportAnimateOptions(
        Vector2? center = null,
        float? zoom = null,
        float? visibleWidth = null,
        float? visibleHeight = null,
        double durationSeconds = 1d,
        EasingKind easing = EasingKind.Linear,
        ViewportMotionInterruptMode interruptMode = ViewportMotionInterruptMode.Cancel)
    {
        ValidateVector(center, nameof(center));
        ValidateOptionalPositive(zoom, nameof(zoom));
        ValidateOptionalPositive(visibleWidth, nameof(visibleWidth));
        ValidateOptionalPositive(visibleHeight, nameof(visibleHeight));
        ValidateDuration(durationSeconds, nameof(durationSeconds));
        ValidateMotionEnums(easing, interruptMode);
        if (center is null && zoom is null && visibleWidth is null && visibleHeight is null)
            throw new ArgumentException("Animate requires a position or zoom target.");
        if (zoom is not null && (visibleWidth is not null || visibleHeight is not null))
            throw new ArgumentException("Use either Zoom or a visible-size target, not both.");
        Center = center;
        Zoom = zoom;
        VisibleWidth = visibleWidth;
        VisibleHeight = visibleHeight;
        DurationSeconds = durationSeconds;
        Easing = easing;
        InterruptMode = interruptMode;
    }

    internal float ResolveZoom(ViewportController controller) =>
        ViewportMotionValidation.ResolveZoom(controller, Zoom, VisibleWidth, VisibleHeight);

    internal void Validate()
    {
        ValidateVector(Center, nameof(Center));
        ValidateOptionalPositive(Zoom, nameof(Zoom));
        ValidateOptionalPositive(VisibleWidth, nameof(VisibleWidth));
        ValidateOptionalPositive(VisibleHeight, nameof(VisibleHeight));
        ValidateDuration(DurationSeconds, nameof(DurationSeconds));
        ValidateMotionEnums(Easing, InterruptMode);
        if (!ChangesPosition && !ChangesZoom)
            throw new ArgumentException("Animate requires a position or zoom target.");
        if (Zoom is not null && (VisibleWidth is not null || VisibleHeight is not null))
            throw new ArgumentException("Use either Zoom or a visible-size target, not both.");
    }

    internal static void ValidateVector(Vector2? value, string name)
    {
        if (value is { } actual && (!float.IsFinite(actual.X) || !float.IsFinite(actual.Y)))
            throw new ArgumentOutOfRangeException(name);
    }

    internal static void ValidateOptionalPositive(float? value, string name)
    {
        if (value is { } actual && (!float.IsFinite(actual) || actual <= 0f))
            throw new ArgumentOutOfRangeException(name);
    }

    internal static void ValidateDuration(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0d)
            throw new ArgumentOutOfRangeException(name);
    }

    internal static void ValidateMotionEnums(
        EasingKind easing,
        ViewportMotionInterruptMode interruptMode)
    {
        if (!Enum.IsDefined(easing)) throw new ArgumentOutOfRangeException(nameof(easing));
        if (!Enum.IsDefined(interruptMode))
            throw new ArgumentOutOfRangeException(nameof(interruptMode));
    }
}

public readonly record struct ViewportSnapOptions
{
    public Vector2 Target { get; }
    public bool UseTopLeft { get; }
    public double DurationSeconds { get; }
    public EasingKind Easing { get; }
    public ViewportMotionInterruptMode InterruptMode { get; }

    public ViewportSnapOptions(
        Vector2 target,
        bool useTopLeft = false,
        double durationSeconds = 1d,
        EasingKind easing = EasingKind.SineInOut,
        ViewportMotionInterruptMode interruptMode = ViewportMotionInterruptMode.Pause)
    {
        ViewportAnimateOptions.ValidateVector(target, nameof(target));
        ViewportAnimateOptions.ValidateDuration(durationSeconds, nameof(durationSeconds));
        ViewportAnimateOptions.ValidateMotionEnums(easing, interruptMode);
        Target = target;
        UseTopLeft = useTopLeft;
        DurationSeconds = durationSeconds;
        Easing = easing;
        InterruptMode = interruptMode;
    }

    internal void Validate()
    {
        ViewportAnimateOptions.ValidateVector(Target, nameof(Target));
        ViewportAnimateOptions.ValidateDuration(DurationSeconds, nameof(DurationSeconds));
        ViewportAnimateOptions.ValidateMotionEnums(Easing, InterruptMode);
    }
}

public readonly record struct ViewportSnapZoomOptions
{
    public float? Zoom { get; }
    public float? VisibleWidth { get; }
    public float? VisibleHeight { get; }
    public Vector2? ViewportAnchor { get; }
    public double DurationSeconds { get; }
    public EasingKind Easing { get; }
    public ViewportMotionInterruptMode InterruptMode { get; }

    public ViewportSnapZoomOptions(
        float? zoom = null,
        float? visibleWidth = null,
        float? visibleHeight = null,
        Vector2? viewportAnchor = null,
        double durationSeconds = 1d,
        EasingKind easing = EasingKind.SineInOut,
        ViewportMotionInterruptMode interruptMode = ViewportMotionInterruptMode.Pause)
    {
        ViewportAnimateOptions.ValidateOptionalPositive(zoom, nameof(zoom));
        ViewportAnimateOptions.ValidateOptionalPositive(visibleWidth, nameof(visibleWidth));
        ViewportAnimateOptions.ValidateOptionalPositive(visibleHeight, nameof(visibleHeight));
        ViewportAnimateOptions.ValidateVector(viewportAnchor, nameof(viewportAnchor));
        ViewportAnimateOptions.ValidateDuration(durationSeconds, nameof(durationSeconds));
        ViewportAnimateOptions.ValidateMotionEnums(easing, interruptMode);
        if (zoom is null && visibleWidth is null && visibleHeight is null)
            throw new ArgumentException("SnapZoom requires a zoom or visible-size target.");
        if (zoom is not null && (visibleWidth is not null || visibleHeight is not null))
            throw new ArgumentException("Use either Zoom or a visible-size target, not both.");
        Zoom = zoom;
        VisibleWidth = visibleWidth;
        VisibleHeight = visibleHeight;
        ViewportAnchor = viewportAnchor;
        DurationSeconds = durationSeconds;
        Easing = easing;
        InterruptMode = interruptMode;
    }

    internal float ResolveZoom(ViewportController controller) =>
        ViewportMotionValidation.ResolveZoom(controller, Zoom, VisibleWidth, VisibleHeight);

    internal void Validate()
    {
        ViewportAnimateOptions.ValidateOptionalPositive(Zoom, nameof(Zoom));
        ViewportAnimateOptions.ValidateOptionalPositive(VisibleWidth, nameof(VisibleWidth));
        ViewportAnimateOptions.ValidateOptionalPositive(VisibleHeight, nameof(VisibleHeight));
        ViewportAnimateOptions.ValidateVector(ViewportAnchor, nameof(ViewportAnchor));
        ViewportAnimateOptions.ValidateDuration(DurationSeconds, nameof(DurationSeconds));
        ViewportAnimateOptions.ValidateMotionEnums(Easing, InterruptMode);
        if (Zoom is null && VisibleWidth is null && VisibleHeight is null)
            throw new ArgumentException("SnapZoom requires a zoom or visible-size target.");
        if (Zoom is not null && (VisibleWidth is not null || VisibleHeight is not null))
            throw new ArgumentException("Use either Zoom or a visible-size target, not both.");
    }
}

public readonly record struct ViewportBounceOptions
{
    public GameEngine.Core.Domain.Gameplay.Bounds2D WorldBounds { get; }
    public ViewportAxis Axis { get; }
    public ViewportUnderflow Underflow { get; }
    public double DurationSeconds { get; }
    public EasingKind Easing { get; }

    public ViewportBounceOptions(
        GameEngine.Core.Domain.Gameplay.Bounds2D worldBounds,
        ViewportAxis axis = ViewportAxis.All,
        ViewportUnderflow underflow = ViewportUnderflow.Center,
        double durationSeconds = 0.15d,
        EasingKind easing = EasingKind.SineInOut)
    {
        if (worldBounds.Width <= 0f || worldBounds.Height <= 0f)
            throw new ArgumentException("Bounce world bounds must have positive area.", nameof(worldBounds));
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!Enum.IsDefined(underflow)) throw new ArgumentOutOfRangeException(nameof(underflow));
        ViewportAnimateOptions.ValidateDuration(durationSeconds, nameof(durationSeconds));
        if (!Enum.IsDefined(easing)) throw new ArgumentOutOfRangeException(nameof(easing));
        WorldBounds = worldBounds;
        Axis = axis;
        Underflow = underflow;
        DurationSeconds = durationSeconds;
        Easing = easing;
    }

    internal void Validate()
    {
        if (WorldBounds.Width <= 0f || WorldBounds.Height <= 0f)
            throw new ArgumentException("Bounce world bounds must have positive area.", nameof(WorldBounds));
        if (!Enum.IsDefined(Axis)) throw new ArgumentOutOfRangeException(nameof(Axis));
        if (!Enum.IsDefined(Underflow)) throw new ArgumentOutOfRangeException(nameof(Underflow));
        ViewportAnimateOptions.ValidateDuration(DurationSeconds, nameof(DurationSeconds));
        if (!Enum.IsDefined(Easing)) throw new ArgumentOutOfRangeException(nameof(Easing));
    }
}

public readonly record struct ViewportEdgeInsets
{
    public float? Left { get; }
    public float? Top { get; }
    public float? Right { get; }
    public float? Bottom { get; }

    public ViewportEdgeInsets(float? left, float? top, float? right, float? bottom)
    {
        ViewportAnimateOptions.ValidateOptionalPositive(left, nameof(left));
        ViewportAnimateOptions.ValidateOptionalPositive(top, nameof(top));
        ViewportAnimateOptions.ValidateOptionalPositive(right, nameof(right));
        ViewportAnimateOptions.ValidateOptionalPositive(bottom, nameof(bottom));
        if (left is null && top is null && right is null && bottom is null)
            throw new ArgumentException("At least one MouseEdges inset is required.");
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static ViewportEdgeInsets Uniform(float distance) =>
        new(distance, distance, distance, distance);

    internal void Validate()
    {
        ViewportAnimateOptions.ValidateOptionalPositive(Left, nameof(Left));
        ViewportAnimateOptions.ValidateOptionalPositive(Top, nameof(Top));
        ViewportAnimateOptions.ValidateOptionalPositive(Right, nameof(Right));
        ViewportAnimateOptions.ValidateOptionalPositive(Bottom, nameof(Bottom));
        if (Left is null && Top is null && Right is null && Bottom is null)
            throw new ArgumentException("At least one MouseEdges inset is required.");
    }
}

public readonly record struct ViewportMouseEdgesOptions
{
    public static ViewportMouseEdgesOptions Default => new(
        ViewportEdgeInsets.Uniform(32f),
        null,
        480f,
        false,
        true,
        false,
        false);

    public ViewportEdgeInsets? Insets { get; }
    public float? Radius { get; }
    public float SpeedPixelsPerSecond { get; }
    public bool Reverse { get; }
    public bool UseDeceleration { get; }
    public bool LinearRadius { get; }
    public bool AllowPointerDown { get; }

    public ViewportMouseEdgesOptions() : this(
        ViewportEdgeInsets.Uniform(32f),
        null,
        480f,
        false,
        true,
        false,
        false) { }

    public ViewportMouseEdgesOptions(
        ViewportEdgeInsets? insets = null,
        float? radius = null,
        float speedPixelsPerSecond = 480f,
        bool reverse = false,
        bool useDeceleration = true,
        bool linearRadius = false,
        bool allowPointerDown = false)
    {
        ViewportAnimateOptions.ValidateOptionalPositive(radius, nameof(radius));
        if (!float.IsFinite(speedPixelsPerSecond) || speedPixelsPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(speedPixelsPerSecond));
        if (insets is not null && radius is not null)
            throw new ArgumentException("MouseEdges uses either edge insets or a center radius.");
        Insets = insets ?? (radius is null ? ViewportEdgeInsets.Uniform(32f) : null);
        Radius = radius;
        SpeedPixelsPerSecond = speedPixelsPerSecond;
        Reverse = reverse;
        UseDeceleration = useDeceleration;
        LinearRadius = linearRadius;
        AllowPointerDown = allowPointerDown;
    }

    internal void Validate()
    {
        ViewportAnimateOptions.ValidateOptionalPositive(Radius, nameof(Radius));
        if (!float.IsFinite(SpeedPixelsPerSecond) || SpeedPixelsPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(SpeedPixelsPerSecond));
        if (Insets is not null && Radius is not null)
            throw new ArgumentException("MouseEdges uses either edge insets or a center radius.");
        if (Insets is null && Radius is null)
            throw new ArgumentException("MouseEdges requires edge insets or a center radius.");
        Insets?.Validate();
    }
}

internal static class ViewportMotionValidation
{
    public static float ResolveZoom(
        ViewportController controller,
        float? zoom,
        float? visibleWidth,
        float? visibleHeight)
    {
        if (zoom is { } direct) return direct;
        float result = float.PositiveInfinity;
        if (visibleWidth is { } width) result = controller.ScreenSize.X / width;
        if (visibleHeight is { } height)
            result = MathF.Min(result, controller.ScreenSize.Y / height);
        if (!float.IsFinite(result) || result <= 0f)
            throw new InvalidOperationException("Viewport visible-size target cannot resolve a zoom.");
        return result;
    }
}
