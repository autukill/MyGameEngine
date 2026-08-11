namespace GameEngine.Features.ViewportNavigation;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;

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
    Pinch = 3,
    Wheel = 4,
    Decelerate = 5,
    ClampZoom = 6,
    Clamp = 7,
    MouseEdges = 8,
    Animate = 9,
    Bounce = 10,
    SnapZoom = 11,
    Snap = 12,
}

/// <summary>One pointer mapped into Render View pixel coordinates.</summary>
public readonly record struct ViewportPointer
{
    public PointerId Id { get; }
    public PointerKind Kind { get; }
    public Vector2 Position { get; }
    public bool IsInside { get; }
    public bool IsCaptured { get; }
    public bool IsDown { get; }
    public bool IsPrimary { get; }
    public bool WasPressed { get; }

    public ViewportPointer(
        PointerId id,
        PointerKind kind,
        Vector2 position,
        bool isInside,
        bool isCaptured,
        bool isDown,
        bool isPrimary = false,
        bool wasPressed = false)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (wasPressed && !isDown)
            throw new ArgumentException("A newly pressed pointer must be down.", nameof(wasPressed));
        Id = id;
        Kind = kind;
        Position = position;
        IsInside = isInside;
        IsCaptured = isCaptured;
        IsDown = isDown;
        IsPrimary = isPrimary;
        WasPressed = wasPressed;
    }
}

/// <summary>
/// Allocation-free multi-pointer input sample in Render View pixel coordinates. The span is valid
/// only for the synchronous <see cref="ViewportController.Update"/> call.
/// </summary>
public readonly ref struct ViewportInputFrame
{
    private readonly ReadOnlySpan<ViewportPointer> _pointers;

    public static ViewportInputFrame Empty => new(
        ReadOnlySpan<ViewportPointer>.Empty,
        Vector2.Zero,
        false,
        0f);

    public ReadOnlySpan<ViewportPointer> Pointers => _pointers;
    public int PointerCount => _pointers.Length;
    public Vector2 ScrollPosition { get; }
    public bool IsScrollInside { get; }
    public float ScrollDelta { get; }

    public ViewportInputFrame(
        ReadOnlySpan<ViewportPointer> pointers,
        Vector2 scrollPosition,
        bool isScrollInside,
        float scrollDelta)
    {
        if (!float.IsFinite(scrollPosition.X) || !float.IsFinite(scrollPosition.Y))
            throw new ArgumentOutOfRangeException(nameof(scrollPosition));
        if (!float.IsFinite(scrollDelta))
            throw new ArgumentOutOfRangeException(nameof(scrollDelta));
        for (int i = 0; i < pointers.Length; i++)
        {
            for (int j = i + 1; j < pointers.Length; j++)
            {
                if (pointers[i].Id == pointers[j].Id)
                    throw new ArgumentException(
                        $"Pointer '{pointers[i].Id}' appears more than once.", nameof(pointers));
            }
        }
        _pointers = pointers;
        ScrollPosition = scrollPosition;
        IsScrollInside = isScrollInside;
        ScrollDelta = scrollDelta;
    }

    public bool TryGetPointer(PointerId id, out ViewportPointer pointer)
    {
        for (int i = 0; i < _pointers.Length; i++)
        {
            if (_pointers[i].Id != id) continue;
            pointer = _pointers[i];
            return true;
        }
        pointer = default;
        return false;
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

public readonly record struct ViewportPinchOptions
{
    public static ViewportPinchOptions Default => new(1f, 1f, true, 2f);

    public float ZoomSpeed { get; }
    public float PanFactor { get; }
    public bool EnablePan { get; }
    public float MinimumDistancePixels { get; }

    public ViewportPinchOptions() : this(1f, 1f, true, 2f) { }

    public ViewportPinchOptions(
        float zoomSpeed = 1f,
        float panFactor = 1f,
        bool enablePan = true,
        float minimumDistancePixels = 2f)
    {
        if (!float.IsFinite(zoomSpeed) || zoomSpeed <= 0f || zoomSpeed > 8f)
            throw new ArgumentOutOfRangeException(nameof(zoomSpeed));
        if (!float.IsFinite(panFactor) || panFactor < 0f || panFactor > 8f)
            throw new ArgumentOutOfRangeException(nameof(panFactor));
        if (!float.IsFinite(minimumDistancePixels) || minimumDistancePixels <= 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumDistancePixels));
        ZoomSpeed = zoomSpeed;
        PanFactor = panFactor;
        EnablePan = enablePan;
        MinimumDistancePixels = minimumDistancePixels;
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
