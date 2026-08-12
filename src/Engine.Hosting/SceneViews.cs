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
/// Scene-owned Camera and interaction policy for one persistent Presentation Render View slot.
/// </summary>
public sealed record SceneRenderViewDefinition
{
    public RenderViewRef View { get; }
    public SceneCameraState Camera { get; }
    public CameraFollowSettings? CameraFollow { get; }
    public ViewportNavigationConfiguration? Navigation { get; }

    internal SceneRenderViewDefinition(
        RenderViewRef view,
        SceneCameraState camera,
        CameraFollowSettings? cameraFollow,
        ViewportNavigationConfiguration? navigation)
    {
        View = view;
        Camera = camera;
        CameraFollow = cameraFollow;
        Navigation = navigation;
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
        Action<ViewportNavigationBuilder>? navigation = null) =>
        Configure(RenderViewRef.Main, camera, cameraFollow, navigation);

    public SceneViewLayoutBuilder Configure(
        RenderViewRef view,
        SceneCameraState camera,
        CameraFollowSettings? cameraFollow = null,
        Action<ViewportNavigationBuilder>? navigation = null)
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
            navigationConfiguration);
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
