namespace GameEngine.Hosting;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>A stable presentation slot. It becomes reusable by true multi-camera Views later.</summary>
public readonly record struct ViewportSlotRef
{
    public static ViewportSlotRef Main => new("main");

    public string Name { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public ViewportSlotRef(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Viewport slot name cannot be empty.", nameof(name));
        Name = name;
    }

    public override string ToString() => Name ?? string.Empty;
}

public sealed record SingleCameraViewportDefinition
{
    public ViewportSlotRef Slot { get; }
    public ViewportRect Viewport { get; }
    public ViewportFitMode Fit { get; }
    public int Layer { get; }
    internal int DeclarationOrder { get; }

    internal SingleCameraViewportDefinition(
        ViewportSlotRef slot,
        ViewportRect viewport,
        ViewportFitMode fit,
        int layer,
        int declarationOrder)
    {
        Slot = slot;
        Viewport = viewport;
        Fit = fit;
        Layer = layer;
        DeclarationOrder = declarationOrder;
    }
}

/// <summary>Builds presentation-only Viewports for the default Camera.</summary>
public sealed class SingleCameraViewportLayoutBuilder
{
    private readonly List<SingleCameraViewportDefinition> _items = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public SingleCameraViewportLayoutBuilder Add(
        string name,
        ViewportRect viewport,
        ViewportFitMode fit = ViewportFitMode.Stretch,
        int? layer = null) => Add(new ViewportSlotRef(name), viewport, fit, layer);

    public SingleCameraViewportLayoutBuilder Add(
        ViewportSlotRef slot,
        ViewportRect viewport,
        ViewportFitMode fit = ViewportFitMode.Stretch,
        int? layer = null)
    {
        if (slot.IsEmpty)
            throw new ArgumentException("Viewport slot cannot be empty.", nameof(slot));
        ValidateViewport(viewport);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        if (!_names.Add(slot.Name))
            throw new ArgumentException($"Viewport slot '{slot}' is already configured.", nameof(slot));
        int order = _items.Count;
        _items.Add(new SingleCameraViewportDefinition(
            slot,
            viewport,
            fit,
            layer ?? order,
            order));
        return this;
    }

    internal IReadOnlyList<SingleCameraViewportDefinition> Build()
    {
        if (_items.Count == 0)
            throw new InvalidOperationException("At least one Viewport slot is required.");
        return Array.AsReadOnly(_items.ToArray());
    }

    internal static IReadOnlyList<SingleCameraViewportDefinition> Default { get; } =
        Array.AsReadOnly(new[]
        {
            new SingleCameraViewportDefinition(
                ViewportSlotRef.Main,
                ViewportRect.FullScreen,
                ViewportFitMode.Stretch,
                0,
                0)
        });

    internal static void ValidateViewport(ViewportRect viewport)
    {
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) ||
            !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height) ||
            viewport.X < 0f || viewport.Y < 0f ||
            viewport.Width <= 0f || viewport.Height <= 0f ||
            viewport.X + viewport.Width > 1f || viewport.Y + viewport.Height > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewport),
                "Viewport must be a positive normalized rectangle inside [0,1].");
        }
    }
}

/// <summary>Resolved pointer coordinates for the topmost matching presentation slot.</summary>
public readonly record struct ViewportHit(
    RenderViewRef View,
    ViewportSlotRef Slot,
    Vector2D ScreenPosition,
    Vector2D ViewPosition,
    Vector2D WorldPosition);

public readonly record struct ViewportSlotDiagnostics(
    RenderViewRef View,
    ViewportSlotRef Slot,
    ViewportRect NormalizedRect,
    ViewportFitMode Fit,
    int Layer,
    int X,
    int Y,
    int Width,
    int Height,
    int RenderWidth,
    int RenderHeight);
