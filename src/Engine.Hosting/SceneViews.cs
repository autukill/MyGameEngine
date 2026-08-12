namespace GameEngine.Hosting;

using System.Collections.ObjectModel;
using System.Numerics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ViewportNavigation;

/// <summary>Initial Camera state owned by one Scene activation.</summary>
public readonly record struct SceneCameraState
{
    public static SceneCameraState Default => new(Vector2.Zero, 1f, 0f);

    public Vector2 Position { get; }
    public float Zoom { get; }
    public float Rotation { get; }

    public SceneCameraState(Vector2 position, float zoom = 1f, float rotation = 0f)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        if (!float.IsFinite(zoom) || zoom <= 0f)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        if (!float.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));
        Position = position;
        Zoom = zoom;
        Rotation = rotation;
    }
}

/// <summary>
/// Controls how a Scene Camera interprets Render View pixel-size changes.
/// </summary>
public enum SceneCameraViewportMode
{
    MatchRenderTarget,
    FixedVisibleHeight,
    FixedVisibleWidth,
    Expand,
    Cover
}

public sealed record SceneCameraViewportPolicy
{
    public static SceneCameraViewportPolicy MatchRenderTarget { get; } =
        new(SceneCameraViewportMode.MatchRenderTarget, Vector2.Zero);

    /// <summary>
    /// Keeps the reference View's visible world height stable while its pixel size changes.
    /// The visible width follows the output aspect ratio and remains centered on the same world point.
    /// </summary>
    public static SceneCameraViewportPolicy FixedVisibleHeight(
        float referenceWidth,
        float visibleHeight)
    {
        if (!float.IsFinite(referenceWidth) || referenceWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(referenceWidth));
        if (!float.IsFinite(visibleHeight) || visibleHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(visibleHeight));
        return Create(
            SceneCameraViewportMode.FixedVisibleHeight,
            referenceWidth,
            visibleHeight);
    }

    /// <summary>
    /// Keeps the reference View's visible world width stable while its pixel size changes.
    /// The visible height follows the output aspect ratio and remains centered.
    /// </summary>
    public static SceneCameraViewportPolicy FixedVisibleWidth(
        float visibleWidth,
        float referenceHeight) =>
        Create(
            SceneCameraViewportMode.FixedVisibleWidth,
            visibleWidth,
            referenceHeight);

    /// <summary>
    /// Keeps the entire reference View visible. The surplus output axis reveals more world.
    /// This is the Camera-framing equivalent of choosing the smaller fit scale.
    /// </summary>
    public static SceneCameraViewportPolicy Expand(
        float referenceWidth,
        float referenceHeight) =>
        Create(SceneCameraViewportMode.Expand, referenceWidth, referenceHeight);

    /// <summary>
    /// Fills the Render View and permits the surplus reference axis to be cropped. This is the
    /// Camera-framing equivalent of choosing the larger fill scale.
    /// </summary>
    public static SceneCameraViewportPolicy Cover(
        float referenceWidth,
        float referenceHeight) =>
        Create(SceneCameraViewportMode.Cover, referenceWidth, referenceHeight);

    public SceneCameraViewportMode Mode { get; }
    public Vector2 ReferenceViewportSize { get; }

    private SceneCameraViewportPolicy(
        SceneCameraViewportMode mode,
        Vector2 referenceViewportSize)
    {
        Mode = mode;
        ReferenceViewportSize = referenceViewportSize;
    }

    internal void Activate(Camera2D camera, in SceneCameraState state)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Vector2 actualSize = camera.ViewportSize;
        camera.Position = state.Position;
        camera.Zoom = state.Zoom;
        camera.Rotation = state.Rotation;
        if (Mode == SceneCameraViewportMode.MatchRenderTarget) return;

        Vector2 reference = ReferenceViewportSize;
        camera.ResizeViewport(reference.X, reference.Y);
        if (!camera.TryViewportToWorld(reference * .5f, out Vector2 referenceCenter))
            throw new InvalidOperationException("Cannot resolve the reference Scene Camera center.");
        camera.ResizeViewport(actualSize.X, actualSize.Y);
        camera.Zoom = CheckedZoom(state.Zoom * ResolveScale(actualSize));
        PreserveCenter(camera, actualSize, referenceCenter);
    }

    internal void Resize(Camera2D camera, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (!float.IsFinite(width) || width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (Mode == SceneCameraViewportMode.MatchRenderTarget)
        {
            camera.ResizeViewport(width, height);
            return;
        }

        Vector2 previousSize = camera.ViewportSize;
        if (!camera.TryViewportToWorld(previousSize * .5f, out Vector2 previousCenter))
            throw new InvalidOperationException("Cannot resolve the current Scene Camera center.");
        float previousScale = ResolveScale(previousSize);
        float nextScale = ResolveScale(new Vector2(width, height));
        float nextZoom = CheckedZoom(camera.Zoom * nextScale / previousScale);
        camera.ResizeViewport(width, height);
        camera.Zoom = nextZoom;
        PreserveCenter(camera, new Vector2(width, height), previousCenter);
    }

    private static SceneCameraViewportPolicy Create(
        SceneCameraViewportMode mode,
        float referenceWidth,
        float referenceHeight)
    {
        if (!Enum.IsDefined(mode) || mode == SceneCameraViewportMode.MatchRenderTarget)
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (!float.IsFinite(referenceWidth) || referenceWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(referenceWidth));
        if (!float.IsFinite(referenceHeight) || referenceHeight <= 0f)
            throw new ArgumentOutOfRangeException(nameof(referenceHeight));
        return new SceneCameraViewportPolicy(
            mode,
            new Vector2(referenceWidth, referenceHeight));
    }

    private float ResolveScale(Vector2 viewportSize)
    {
        float horizontal = viewportSize.X / ReferenceViewportSize.X;
        float vertical = viewportSize.Y / ReferenceViewportSize.Y;
        return Mode switch
        {
            SceneCameraViewportMode.FixedVisibleHeight => vertical,
            SceneCameraViewportMode.FixedVisibleWidth => horizontal,
            SceneCameraViewportMode.Expand => MathF.Min(horizontal, vertical),
            SceneCameraViewportMode.Cover => MathF.Max(horizontal, vertical),
            _ => 1f
        };
    }

    private static float CheckedZoom(float value)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new InvalidOperationException("Scene Camera resize produced an invalid Zoom.");
        return value;
    }

    private static void PreserveCenter(Camera2D camera, Vector2 viewportSize, Vector2 targetCenter)
    {
        if (!camera.TryViewportToWorld(viewportSize * .5f, out Vector2 currentCenter))
            throw new InvalidOperationException("Cannot resolve the resized Scene Camera center.");
        camera.Position += targetCenter - currentCenter;
    }
}

/// <summary>
/// Scene-owned Camera and interaction policy for one persistent Presentation Render View slot.
/// </summary>
public sealed record SceneRenderViewDefinition
{
    public RenderViewRef View { get; }
    public SceneCameraState Camera { get; }
    public CameraFollowSettings? CameraFollow { get; }
    public ViewportNavigationConfiguration? Navigation { get; }
    public SceneCameraViewportPolicy ViewportPolicy { get; }

    internal SceneRenderViewDefinition(
        RenderViewRef view,
        SceneCameraState camera,
        CameraFollowSettings? cameraFollow,
        ViewportNavigationConfiguration? navigation,
        SceneCameraViewportPolicy viewportPolicy)
    {
        View = view;
        Camera = camera;
        CameraFollow = cameraFollow;
        Navigation = navigation;
        ViewportPolicy = viewportPolicy;
    }
}

/// <summary>
/// Declares the Camera and navigation policies activated with a Scene. Render targets, output
/// rectangles and post-processing remain part of the application's persistent renderer layout.
/// </summary>
public sealed class SceneViewLayoutBuilder
{
    private readonly Dictionary<string, SceneRenderViewDefinition> _views =
        new(StringComparer.Ordinal);

    public SceneViewLayoutBuilder ConfigureMain(
        SceneCameraState camera,
        CameraFollowSettings? cameraFollow = null,
        Action<ViewportNavigationBuilder>? navigation = null,
        SceneCameraViewportPolicy? viewportPolicy = null) =>
        Configure(RenderViewRef.Main, camera, cameraFollow, navigation, viewportPolicy);

    public SceneViewLayoutBuilder Configure(
        RenderViewRef view,
        SceneCameraState camera,
        CameraFollowSettings? cameraFollow = null,
        Action<ViewportNavigationBuilder>? navigation = null,
        SceneCameraViewportPolicy? viewportPolicy = null)
    {
        if (view.IsEmpty)
            throw new ArgumentException("Scene Render View reference cannot be empty.", nameof(view));
        if (camera == default)
            throw new ArgumentException(
                "Scene Camera state must be explicitly initialized; use SceneCameraState.Default.",
                nameof(camera));
        if (cameraFollow is not null && navigation is not null)
            throw new ArgumentException(
                "A Scene Render View cannot combine CameraFollow and interactive navigation.");

        ViewportNavigationConfiguration? navigationConfiguration = null;
        if (navigation is not null)
        {
            var builder = new ViewportNavigationBuilder();
            navigation(builder);
            navigationConfiguration = builder.Build();
        }

        var definition = new SceneRenderViewDefinition(
            view,
            camera,
            cameraFollow,
            navigationConfiguration,
            viewportPolicy ?? SceneCameraViewportPolicy.MatchRenderTarget);
        if (!_views.TryAdd(view.Name, definition))
            throw new InvalidOperationException(
                $"Scene Render View '{view}' is already configured.");
        return this;
    }

    internal IReadOnlyDictionary<string, SceneRenderViewDefinition> Build()
    {
        if (_views.Count == 0)
            throw new InvalidOperationException(
                "Scene View configuration requires at least one Render View.");
        return new ReadOnlyDictionary<string, SceneRenderViewDefinition>(
            new Dictionary<string, SceneRenderViewDefinition>(_views, StringComparer.Ordinal));
    }
}
