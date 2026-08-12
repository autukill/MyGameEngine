namespace GameEngine.Hosting;

using System.Collections.ObjectModel;
using System.Numerics;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
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

/// <summary>Controls what happens when aspect-ratio expansion reaches an authored world limit.</summary>
public enum SceneCameraFramingOverflow
{
    Unbounded,
    Letterbox
}

/// <summary>
/// Pure result of resolving one Scene Camera policy against an output pixel size. ContentSize is
/// the RenderTarget size; ContentRect is its normalized placement inside the original output slot.
/// </summary>
public readonly record struct SceneCameraFramingResult
{
    public int OutputWidth { get; }
    public int OutputHeight { get; }
    public int ContentWidth { get; }
    public int ContentHeight { get; }
    public float Scale { get; }
    public Vector2 VisibleWorldSize { get; }
    public Vector2 Anchor { get; }
    public ViewportRect ContentRect { get; }
    public bool HasLetterbox => ContentWidth != OutputWidth || ContentHeight != OutputHeight;

    internal SceneCameraFramingResult(
        int outputWidth,
        int outputHeight,
        int contentWidth,
        int contentHeight,
        float scale,
        Vector2 visibleWorldSize,
        Vector2 anchor)
    {
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
        Scale = scale;
        VisibleWorldSize = visibleWorldSize;
        Anchor = anchor;
        float normalizedWidth = (float)contentWidth / outputWidth;
        float normalizedHeight = (float)contentHeight / outputHeight;
        ContentRect = new ViewportRect(
            (1f - normalizedWidth) * .5f,
            (1f - normalizedHeight) * .5f,
            normalizedWidth,
            normalizedHeight);
    }
}

public sealed record SceneCameraViewportPolicy
{
    public static SceneCameraViewportPolicy MatchRenderTarget { get; } =
        new(
            SceneCameraViewportMode.MatchRenderTarget,
            Vector2.Zero,
            new Vector2(.5f),
            null,
            SceneCameraFramingOverflow.Unbounded);

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
    public Vector2 Anchor { get; }
    public Vector2? MaximumVisibleSize { get; }
    public SceneCameraFramingOverflow Overflow { get; }
    public bool IsBounded => MaximumVisibleSize.HasValue;

    private SceneCameraViewportPolicy(
        SceneCameraViewportMode mode,
        Vector2 referenceViewportSize,
        Vector2 anchor,
        Vector2? maximumVisibleSize,
        SceneCameraFramingOverflow overflow)
    {
        Mode = mode;
        ReferenceViewportSize = referenceViewportSize;
        Anchor = anchor;
        MaximumVisibleSize = maximumVisibleSize;
        Overflow = overflow;
    }

    /// <summary>
    /// Preserves the world point at a normalized content coordinate during activation and resize.
    /// (0,0) is top-left, (.5,.5) is center and (1,1) is bottom-right.
    /// </summary>
    public SceneCameraViewportPolicy WithAnchor(float x, float y)
    {
        ValidateAnchor(x, nameof(x));
        ValidateAnchor(y, nameof(y));
        return new SceneCameraViewportPolicy(
            Mode,
            ReferenceViewportSize,
            new Vector2(x, y),
            MaximumVisibleSize,
            Overflow);
    }

    /// <summary>
    /// Caps aspect-ratio expansion and presents the remaining output area as letterbox bars.
    /// v1 intentionally supports the non-cropping FixedVisibleHeight and Expand modes.
    /// </summary>
    public SceneCameraViewportPolicy WithMaximumVisibleSize(
        float width,
        float height,
        SceneCameraFramingOverflow overflow = SceneCameraFramingOverflow.Letterbox)
    {
        if (Mode is not SceneCameraViewportMode.FixedVisibleHeight and
            not SceneCameraViewportMode.Expand)
            throw new InvalidOperationException(
                "Visible-size limits currently support FixedVisibleHeight and Expand policies.");
        if (!float.IsFinite(width) || width < ReferenceViewportSize.X)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(height) || height < ReferenceViewportSize.Y)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (overflow != SceneCameraFramingOverflow.Letterbox)
            throw new ArgumentOutOfRangeException(
                nameof(overflow), "A visible-size limit requires Letterbox overflow behavior.");
        return new SceneCameraViewportPolicy(
            Mode,
            ReferenceViewportSize,
            Anchor,
            new Vector2(width, height),
            overflow);
    }

    /// <summary>Resolves framing without mutating a Camera or allocating GPU resources.</summary>
    public SceneCameraFramingResult Resolve(int outputWidth, int outputHeight)
    {
        if (outputWidth <= 0) throw new ArgumentOutOfRangeException(nameof(outputWidth));
        if (outputHeight <= 0) throw new ArgumentOutOfRangeException(nameof(outputHeight));
        var output = new Vector2(outputWidth, outputHeight);
        float scale = ResolveScale(output);
        Vector2 desiredVisible = output / scale;
        int contentWidth = outputWidth;
        int contentHeight = outputHeight;
        if (MaximumVisibleSize is { } maximum)
        {
            Vector2 limited = Vector2.Min(desiredVisible, maximum);
            contentWidth = Math.Clamp(
                (int)MathF.Round(limited.X * scale, MidpointRounding.AwayFromZero),
                1,
                outputWidth);
            contentHeight = Math.Clamp(
                (int)MathF.Round(limited.Y * scale, MidpointRounding.AwayFromZero),
                1,
                outputHeight);
        }
        var visible = new Vector2(contentWidth / scale, contentHeight / scale);
        return new SceneCameraFramingResult(
            outputWidth,
            outputHeight,
            contentWidth,
            contentHeight,
            scale,
            visible,
            Anchor);
    }

    internal SceneCameraFramingResult Activate(
        Camera2D camera,
        in SceneCameraState state,
        int outputWidth,
        int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(camera);
        SceneCameraFramingResult result = Resolve(outputWidth, outputHeight);
        camera.Position = state.Position;
        camera.Zoom = state.Zoom;
        camera.Rotation = state.Rotation;
        if (Mode == SceneCameraViewportMode.MatchRenderTarget)
        {
            camera.ResizeViewport(result.ContentWidth, result.ContentHeight);
            return result;
        }

        Vector2 reference = ReferenceViewportSize;
        camera.ResizeViewport(reference.X, reference.Y);
        if (!camera.TryViewportToWorld(reference * Anchor, out Vector2 referenceAnchor))
            throw new InvalidOperationException("Cannot resolve the reference Scene Camera anchor.");
        camera.ResizeViewport(result.ContentWidth, result.ContentHeight);
        camera.Zoom = CheckedZoom(state.Zoom * result.Scale);
        PreserveAnchor(camera, result, referenceAnchor);
        return result;
    }

    internal SceneCameraFramingResult Activate(Camera2D camera, in SceneCameraState state) =>
        Activate(
            camera,
            state,
            CheckedPixelSize(camera.ViewportSize.X),
            CheckedPixelSize(camera.ViewportSize.Y));

    internal SceneCameraFramingResult Resize(
        Camera2D camera,
        in SceneCameraFramingResult previous,
        int outputWidth,
        int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(camera);
        SceneCameraFramingResult next = Resolve(outputWidth, outputHeight);
        if (Mode == SceneCameraViewportMode.MatchRenderTarget)
        {
            camera.ResizeViewport(next.ContentWidth, next.ContentHeight);
            return next;
        }

        if (!camera.TryViewportToWorld(
                new Vector2(previous.ContentWidth, previous.ContentHeight) * Anchor,
                out Vector2 previousAnchor))
            throw new InvalidOperationException("Cannot resolve the current Scene Camera anchor.");
        float nextZoom = CheckedZoom(camera.Zoom * next.Scale / previous.Scale);
        camera.ResizeViewport(next.ContentWidth, next.ContentHeight);
        camera.Zoom = nextZoom;
        PreserveAnchor(camera, next, previousAnchor);
        return next;
    }

    internal SceneCameraFramingResult Resize(Camera2D camera, float width, float height)
    {
        var previous = Resolve(
            CheckedPixelSize(camera.ViewportSize.X),
            CheckedPixelSize(camera.ViewportSize.Y));
        return Resize(
            camera,
            previous,
            CheckedPixelSize(width),
            CheckedPixelSize(height));
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
            new Vector2(referenceWidth, referenceHeight),
            new Vector2(.5f),
            null,
            SceneCameraFramingOverflow.Unbounded);
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

    private static void PreserveAnchor(
        Camera2D camera,
        in SceneCameraFramingResult result,
        Vector2 targetAnchor)
    {
        var viewportAnchor = new Vector2(result.ContentWidth, result.ContentHeight) * result.Anchor;
        if (!camera.TryViewportToWorld(viewportAnchor, out Vector2 currentAnchor))
            throw new InvalidOperationException("Cannot resolve the resized Scene Camera anchor.");
        camera.Position += targetAnchor - currentAnchor;
    }

    private static void ValidateAnchor(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static int CheckedPixelSize(float value)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(nameof(value));
        return Math.Max(1, (int)MathF.Round(value, MidpointRounding.AwayFromZero));
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
