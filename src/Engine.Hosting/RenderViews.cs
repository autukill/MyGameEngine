namespace GameEngine.Hosting;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ViewportNavigation;

public readonly record struct RenderViewRef
{
    public static RenderViewRef Main => new("main");

    public string Name { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public RenderViewRef(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Render View name cannot be empty.", nameof(name));
        Name = name;
    }

    public override string ToString() => Name ?? string.Empty;
}

public sealed record RenderViewDefinition
{
    public RenderViewRef Ref { get; }
    public ViewportSlotRef Slot { get; }
    public ViewportRect Viewport { get; }
    public ViewportFitMode Fit { get; }
    public float RenderScale { get; }
    public int Layer { get; }
    public SceneLayerFilter SceneLayers { get; }
    public RenderViewEffects Effects { get; }
    public CameraFollowSettings? CameraFollow { get; }
    public ViewportNavigationConfiguration? Navigation { get; }
    internal int DeclarationOrder { get; }

    internal RenderViewDefinition(
        RenderViewRef reference,
        ViewportRect viewport,
        ViewportFitMode fit,
        float renderScale,
        int layer,
        SceneLayerFilter sceneLayers,
        RenderViewEffects effects,
        CameraFollowSettings? cameraFollow,
        ViewportNavigationConfiguration? navigation,
        int declarationOrder)
    {
        Ref = reference;
        Slot = new ViewportSlotRef(reference.Name);
        Viewport = viewport;
        Fit = fit;
        RenderScale = renderScale;
        Layer = layer;
        SceneLayers = sceneLayers;
        Effects = effects;
        CameraFollow = cameraFollow;
        Navigation = navigation;
        DeclarationOrder = declarationOrder;
    }
}

/// <summary>Declares independently rendered Camera views. Main remains the primary effects view.</summary>
public sealed class RenderViewLayoutBuilder
{
    private readonly List<RenderViewDefinition> _secondary = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal) { RenderViewRef.Main.Name };
    private RenderViewDefinition _main = Create(
        RenderViewRef.Main,
        ViewportRect.FullScreen,
        ViewportFitMode.Stretch,
        1f,
        0,
        SceneLayerFilter.All,
        RenderViewEffects.Direct,
        null,
        null,
        0);
    private bool _mainConfigured;

    public RenderViewLayoutBuilder ConfigureMain(
        ViewportRect viewport,
        float renderScale = 1f,
        ViewportFitMode fit = ViewportFitMode.Stretch,
        int layer = 0,
        SceneLayerFilter? sceneLayers = null,
        CameraFollowSettings? cameraFollow = null,
        Action<ViewportNavigationBuilder>? navigation = null)
    {
        if (_mainConfigured)
            throw new InvalidOperationException("The main Render View is already configured.");
        _main = Create(
            RenderViewRef.Main,
            viewport,
            fit,
            renderScale,
            layer,
            sceneLayers ?? SceneLayerFilter.All,
            RenderViewEffects.Direct,
            cameraFollow,
            BuildNavigation(navigation),
            0);
        _mainConfigured = true;
        return this;
    }

    public RenderViewLayoutBuilder Add(
        string name,
        ViewportRect viewport,
        float renderScale = 1f,
        ViewportFitMode fit = ViewportFitMode.Stretch,
        int? layer = null,
        SceneLayerFilter? sceneLayers = null,
        RenderViewEffects? effects = null,
        CameraFollowSettings? cameraFollow = null,
        Action<ViewportNavigationBuilder>? navigation = null)
    {
        var reference = new RenderViewRef(name);
        if (!_names.Add(reference.Name))
            throw new ArgumentException($"Render View '{reference}' is already configured.", nameof(name));
        int order = _secondary.Count + 1;
        _secondary.Add(Create(
            reference,
            viewport,
            fit,
            renderScale,
            layer ?? order,
            sceneLayers ?? SceneLayerFilter.All,
            effects ?? RenderViewEffects.Direct,
            cameraFollow,
            BuildNavigation(navigation),
            order));
        return this;
    }

    internal IReadOnlyList<RenderViewDefinition> Build()
    {
        if (_secondary.Count == 0)
            throw new InvalidOperationException(
                "UseRenderViews requires at least one additional Render View.");
        var result = new RenderViewDefinition[_secondary.Count + 1];
        result[0] = _main;
        _secondary.CopyTo(result, 1);
        return Array.AsReadOnly(result);
    }

    internal static (int Width, int Height) ResolveRenderSize(
        ViewportRect viewport,
        float renderScale,
        int screenWidth,
        int screenHeight)
    {
        var (_, _, slotWidth, slotHeight) = viewport.ToPixels(screenWidth, screenHeight);
        return (
            Math.Max(1, (int)MathF.Round(
                slotWidth * renderScale,
                MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)MathF.Round(
                slotHeight * renderScale,
                MidpointRounding.AwayFromZero)));
    }

    private static RenderViewDefinition Create(
        RenderViewRef reference,
        ViewportRect viewport,
        ViewportFitMode fit,
        float renderScale,
        int layer,
        SceneLayerFilter sceneLayers,
        RenderViewEffects effects,
        CameraFollowSettings? cameraFollow,
        ViewportNavigationConfiguration? navigation,
        int order)
    {
        SingleCameraViewportLayoutBuilder.ValidateViewport(viewport);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        if (!float.IsFinite(renderScale) || renderScale <= 0f || renderScale > 1f)
            throw new ArgumentOutOfRangeException(
                nameof(renderScale), "Render scale must be in (0, 1].");
        if (cameraFollow is not null && navigation is not null)
            throw new ArgumentException(
                "A Render View cannot combine CameraFollow and interactive Viewport navigation.");
        return new RenderViewDefinition(
            reference,
            viewport,
            fit,
            renderScale,
            layer,
            sceneLayers,
            effects,
            cameraFollow,
            navigation,
            order);
    }

    private static ViewportNavigationConfiguration? BuildNavigation(
        Action<ViewportNavigationBuilder>? configure)
    {
        if (configure is null) return null;
        var builder = new ViewportNavigationBuilder();
        configure(builder);
        return builder.Build();
    }

    internal static RenderViewDefinition WithEffects(
        RenderViewDefinition definition,
        RenderViewEffects effects) => new(
            definition.Ref,
            definition.Viewport,
            definition.Fit,
            definition.RenderScale,
            definition.Layer,
            definition.SceneLayers,
            effects,
            definition.CameraFollow,
            definition.Navigation,
            definition.DeclarationOrder);

    internal static RenderViewDefinition WithNavigation(
        RenderViewDefinition definition,
        ViewportNavigationConfiguration navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (definition.CameraFollow is not null)
            throw new ArgumentException(
                "A Render View cannot combine CameraFollow and interactive Viewport navigation.");
        return new RenderViewDefinition(
            definition.Ref,
            definition.Viewport,
            definition.Fit,
            definition.RenderScale,
            definition.Layer,
            definition.SceneLayers,
            definition.Effects,
            null,
            navigation,
            definition.DeclarationOrder);
    }
}

/// <summary>A logical View exposed to gameplay without exposing its RenderTarget.</summary>
public sealed class RenderView
{
    private readonly RenderTarget2D _target;
    private readonly CameraFollowSettings? _defaultCameraFollow;
    private readonly ViewportNavigationConfiguration? _defaultNavigation;
    private SceneRenderPass? _scenePass;

    public RenderViewRef Ref { get; }
    public ViewportSlotRef Slot { get; }
    public Camera2D Camera { get; }
    public CameraFollowController? CameraFollow { get; private set; }
    public ViewportController? Navigation { get; private set; }
    public ViewportRect Viewport { get; }
    public ViewportFitMode Fit { get; }
    public float RenderScale { get; }
    public int Layer { get; }
    public SceneLayerFilter SceneLayers { get; }
    public RenderViewEffects Effects { get; }
    public RenderSurfaceKey SceneColor { get; }
    public RenderSurfaceKey DisplayColor { get; }
    public SceneDrawStatistics LastSceneDraw => _scenePass?.LastDrawStatistics ?? default;
    public Vector2D RenderSize => new(_target.Width, _target.Height);
    internal RenderTarget2D Target => _target;
    internal int DeclarationOrder { get; }

    internal RenderView(
        RenderViewDefinition definition,
        Camera2D camera,
        RenderTarget2D target)
    {
        Ref = definition.Ref;
        Slot = definition.Slot;
        Camera = camera;
        _defaultCameraFollow = definition.CameraFollow;
        _defaultNavigation = definition.Navigation;
        Viewport = definition.Viewport;
        Fit = definition.Fit;
        RenderScale = definition.RenderScale;
        Layer = definition.Layer;
        SceneLayers = definition.SceneLayers;
        Effects = definition.Effects;
        DeclarationOrder = definition.DeclarationOrder;
        SceneColor = definition.Ref == RenderViewRef.Main
            ? RenderSurfaceKey.SceneColor
            : new RenderSurfaceKey("scene-view", definition.Ref.Name, "color");
        DisplayColor = Effects.IsHdr
            ? ToneMappingEffectDescriptor.ColorOutput(
                new RenderEffectKey(ToneMappingEffectDescriptor.EffectKind, definition.Ref.Name))
            : SceneColor;
        _target = target;
    }

    /// <summary>Returns the declaratively configured follow controller for this View.</summary>
    public CameraFollowController RequireCameraFollow() => CameraFollow ??
        throw new InvalidOperationException(
            $"Render View '{Ref}' does not declare a Camera follow policy.");

    /// <summary>Returns the declaratively configured interactive Viewport controller.</summary>
    public ViewportController RequireNavigation() => Navigation ??
        throw new InvalidOperationException(
            $"Render View '{Ref}' does not declare interactive Viewport navigation.");

    internal void ActivateScene(SceneRenderViewDefinition? configuration)
    {
        Navigation?.Plugins.RemoveAll();
        SceneCameraState camera = configuration?.Camera ?? SceneCameraState.Default;
        Camera.Shake(0f, 0f);
        Camera.Position = camera.Position;
        Camera.Zoom = camera.Zoom;
        Camera.Rotation = camera.Rotation;

        CameraFollowSettings? follow = configuration is null
            ? _defaultCameraFollow
            : configuration.CameraFollow;
        ViewportNavigationConfiguration? navigation = configuration is null
            ? _defaultNavigation
            : configuration.Navigation;
        CameraFollow = follow is { } settings
            ? new CameraFollowController(Camera, settings)
            : null;
        Navigation = navigation?.CreateController(Camera);
    }

    internal void AttachScenePass(SceneRenderPass scenePass)
    {
        ArgumentNullException.ThrowIfNull(scenePass);
        if (_scenePass is not null)
            throw new InvalidOperationException($"Render View '{Ref}' already has a Scene Pass.");
        _scenePass = scenePass;
    }
}
