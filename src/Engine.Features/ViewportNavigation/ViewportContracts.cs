namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;

public enum ViewportAxis
{
    All = 0,
    Horizontal = 1,
    Vertical = 2,
}

public enum ViewportUnderflow
{
    None = 0,
    Center = 1,
    TopLeft = 2,
    Top = 3,
    TopRight = 4,
    Left = 5,
    Right = 6,
    BottomLeft = 7,
    Bottom = 8,
    BottomRight = 9,
}

public enum ViewportChangeKind
{
    Programmatic = 0,
    Resize = 1,
    Drag = 2,
    Wheel = 3,
    Decelerate = 4,
    ClampZoom = 5,
    Clamp = 6,
}

/// <summary>One input sample in Render View pixel coordinates.</summary>
public readonly record struct ViewportInputFrame
{
    public static ViewportInputFrame Empty { get; } = new(Vector2.Zero, false, false, 0f);

    public Vector2 PointerPosition { get; }
    public bool IsPointerInside { get; }
    public bool PrimaryDown { get; }
    public float ScrollDelta { get; }

    public ViewportInputFrame(
        Vector2 pointerPosition,
        bool isPointerInside,
        bool primaryDown,
        float scrollDelta)
    {
        if (!float.IsFinite(pointerPosition.X) || !float.IsFinite(pointerPosition.Y))
            throw new ArgumentOutOfRangeException(nameof(pointerPosition));
        if (!float.IsFinite(scrollDelta))
            throw new ArgumentOutOfRangeException(nameof(scrollDelta));
        PointerPosition = pointerPosition;
        IsPointerInside = isPointerInside;
        PrimaryDown = primaryDown;
        ScrollDelta = scrollDelta;
    }
}

/// <summary>Stable, allocation-free observation boundary for culling and future world streaming.</summary>
public readonly record struct ViewportSnapshot(
    Bounds2D VisibleWorldBounds,
    Vector2 Center,
    float Zoom,
    Vector2 ScreenSize,
    ulong Revision);

public readonly record struct ViewportChangedEvent(
    ViewportChangeKind Kind,
    Vector2 PreviousCenter,
    Vector2 Center,
    float PreviousZoom,
    float Zoom,
    ulong Revision);

public readonly record struct ViewportDragOptions
{
    public static ViewportDragOptions Default => new(ViewportAxis.All, 0f);

    public ViewportAxis Axis { get; }
    public float ThresholdPixels { get; }

    public ViewportDragOptions() : this(ViewportAxis.All, 0f) { }

    public ViewportDragOptions(
        ViewportAxis axis = ViewportAxis.All,
        float thresholdPixels = 0f)
    {
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!float.IsFinite(thresholdPixels) || thresholdPixels < 0f)
            throw new ArgumentOutOfRangeException(nameof(thresholdPixels));
        Axis = axis;
        ThresholdPixels = thresholdPixels;
    }
}

public readonly record struct ViewportWheelOptions
{
    public static ViewportWheelOptions Default => new(0.1f, 0, true, false);

    public float Percent { get; }
    public int SmoothFrames { get; }
    public bool InterruptOnPointerDown { get; }
    public bool Reverse { get; }

    public ViewportWheelOptions() : this(0.1f, 0, true, false) { }

    public ViewportWheelOptions(
        float percent = 0.1f,
        int smoothFrames = 0,
        bool interruptOnPointerDown = true,
        bool reverse = false)
    {
        if (!float.IsFinite(percent) || percent <= 0f || percent > 4f)
            throw new ArgumentOutOfRangeException(nameof(percent));
        if (smoothFrames < 0 || smoothFrames > 240)
            throw new ArgumentOutOfRangeException(nameof(smoothFrames));
        Percent = percent;
        SmoothFrames = smoothFrames;
        InterruptOnPointerDown = interruptOnPointerDown;
        Reverse = reverse;
    }
}

public readonly record struct ViewportDecelerateOptions
{
    public static ViewportDecelerateOptions Default => new(0.98f, 1f);

    public float Friction { get; }
    public float MinimumSpeed { get; }

    public ViewportDecelerateOptions() : this(0.98f, 1f) { }

    public ViewportDecelerateOptions(float friction = 0.98f, float minimumSpeed = 1f)
    {
        if (!float.IsFinite(friction) || friction <= 0f || friction >= 1f)
            throw new ArgumentOutOfRangeException(nameof(friction));
        if (!float.IsFinite(minimumSpeed) || minimumSpeed < 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumSpeed));
        Friction = friction;
        MinimumSpeed = minimumSpeed;
    }
}

/// <summary>
/// Zoom constraints. Width/height values describe the visible world span, matching pixi-viewport's
/// clampZoom vocabulary. MaxWidth therefore establishes a minimum zoom.
/// </summary>
public readonly record struct ViewportClampZoomOptions
{
    public float? MinWidth { get; }
    public float? MinHeight { get; }
    public float? MaxWidth { get; }
    public float? MaxHeight { get; }
    public float? MinScale { get; }
    public float? MaxScale { get; }

    public ViewportClampZoomOptions(
        float? minWidth = null,
        float? minHeight = null,
        float? maxWidth = null,
        float? maxHeight = null,
        float? minScale = null,
        float? maxScale = null)
    {
        ValidateOptionalPositive(minWidth, nameof(minWidth));
        ValidateOptionalPositive(minHeight, nameof(minHeight));
        ValidateOptionalPositive(maxWidth, nameof(maxWidth));
        ValidateOptionalPositive(maxHeight, nameof(maxHeight));
        ValidateOptionalPositive(minScale, nameof(minScale));
        ValidateOptionalPositive(maxScale, nameof(maxScale));
        if (minWidth is not null && maxWidth is not null && minWidth > maxWidth)
            throw new ArgumentException("Minimum visible width cannot exceed maximum visible width.");
        if (minHeight is not null && maxHeight is not null && minHeight > maxHeight)
            throw new ArgumentException("Minimum visible height cannot exceed maximum visible height.");
        if (minScale is not null && maxScale is not null && minScale > maxScale)
            throw new ArgumentException("Minimum scale cannot exceed maximum scale.");
        MinWidth = minWidth;
        MinHeight = minHeight;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MinScale = minScale;
        MaxScale = maxScale;
    }

    private static void ValidateOptionalPositive(float? value, string name)
    {
        if (value is { } actual && (!float.IsFinite(actual) || actual <= 0f))
            throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct ViewportClampOptions
{
    public Bounds2D WorldBounds { get; }
    public ViewportAxis Axis { get; }
    public ViewportUnderflow Underflow { get; }

    public ViewportClampOptions(
        Bounds2D worldBounds,
        ViewportAxis axis = ViewportAxis.All,
        ViewportUnderflow underflow = ViewportUnderflow.Center)
    {
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!Enum.IsDefined(underflow)) throw new ArgumentOutOfRangeException(nameof(underflow));
        if (worldBounds.Width <= 0f || worldBounds.Height <= 0f)
            throw new ArgumentException("Viewport world bounds must have positive area.", nameof(worldBounds));
        WorldBounds = worldBounds;
        Axis = axis;
        Underflow = underflow;
    }
}
