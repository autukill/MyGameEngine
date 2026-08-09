namespace GameEngine.Hosting;

using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;

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
        0);
    private bool _mainConfigured;

    public RenderViewLayoutBuilder ConfigureMain(
        ViewportRect viewport,
        float renderScale = 1f,
        ViewportFitMode fit = ViewportFitMode.Stretch,
        int layer = 0,
        SceneLayerFilter? sceneLayers = null,
        CameraFollowSettings? cameraFollow = null)
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
        CameraFollowSettings? cameraFollow = null)
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
        int order)
    {
        SingleCameraViewportLayoutBuilder.ValidateViewport(viewport);
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));
        if (!float.IsFinite(renderScale) || renderScale <= 0f || renderScale > 1f)
            throw new ArgumentOutOfRangeException(
                nameof(renderScale), "Render scale must be in (0, 1].");
        return new RenderViewDefinition(
            reference,
            viewport,
            fit,
            renderScale,
            layer,
            sceneLayers,
            effects,
            cameraFollow,
            order);
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
            definition.DeclarationOrder);
}

/// <summary>A logical View exposed to gameplay without exposing its RenderTarget.</summary>
public sealed class RenderView
{
    private readonly RenderTarget2D _target;
    private SceneRenderPass? _scenePass;

    public RenderViewRef Ref { get; }
    public ViewportSlotRef Slot { get; }
    public Camera2D Camera { get; }
    public CameraFollowController? CameraFollow { get; }
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
        CameraFollow = definition.CameraFollow is { } settings
            ? new CameraFollowController(camera, settings)
            : null;
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

    internal void AttachScenePass(SceneRenderPass scenePass)
    {
        ArgumentNullException.ThrowIfNull(scenePass);
        if (_scenePass is not null)
            throw new InvalidOperationException($"Render View '{Ref}' already has a Scene Pass.");
        _scenePass = scenePass;
    }
}
