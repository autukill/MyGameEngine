namespace GameEngine.Hosting;

using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ShaderAssets.Domain;
using GameEngine.Features.ShaderAssets.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.ViewportNavigation;

/// <summary>默认 2D 渲染预设；只有显式启用的可选 Feature 才创建 GPU 资源。</summary>
public sealed class Default2DRendererOptions
{
    private string? _contentPackagesRoot;
    private string? _contentManifest;
    private ContentPackageRef? _contentPackage;
    private bool _contentCatalogOnly;
    private bool _hdrEnabled;
    private ToneMappingSettings _toneMapping = ToneMappingSettings.Default;
    private BloomSettings? _bloom;
    private bool _stencilMaskingEnabled;
    private bool _sceneGuiEnabled = true;
    private PerformanceTelemetryOptions? _performanceTelemetry;
    private ContentHotReloadOptions? _contentHotReload;
    private string? _shaderRoot;
    private readonly List<ShaderFileDefinition> _shaderFiles = [];
    private readonly List<MaterialAssetDefinition> _shaderMaterials = [];
    private string? _shaderAssetManifestPath;
    private ShaderHotReloadOptions? _shaderHotReload;
    private IReadOnlyList<SingleCameraViewportDefinition>? _viewports;
    private IReadOnlyList<RenderViewDefinition>? _renderViews;
    private ViewportNavigationConfiguration? _mainNavigation;

    public Default2DRendererOptions UseContent(
        string packagesRoot,
        string manifestPath = "assets.json")
    {
        if (string.IsNullOrWhiteSpace(packagesRoot))
            throw new ArgumentException("Content packages root cannot be empty.", nameof(packagesRoot));
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Content manifest path cannot be empty.", nameof(manifestPath));
        _contentPackagesRoot = packagesRoot;
        _contentManifest = manifestPath;
        _contentPackage = null;
        _contentCatalogOnly = false;
        return this;
    }

    public Default2DRendererOptions UseContent(
        ContentPackageRef package,
        string packagesRoot = "AssetsCompiled")
    {
        if (string.IsNullOrWhiteSpace(packagesRoot))
            throw new ArgumentException("Content packages root cannot be empty.", nameof(packagesRoot));
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Manifest))
            throw new ArgumentException("Content package reference cannot be empty.", nameof(package));
        _contentPackagesRoot = packagesRoot;
        _contentManifest = package.Manifest;
        _contentPackage = package;
        _contentCatalogOnly = false;
        return this;
    }

    /// <summary>
    /// Configures a package directory without eagerly loading a root package. Scenes declare their
    /// own package through the matching AddScene overload and hold it only while active.
    /// </summary>
    public Default2DRendererOptions UseContentCatalog(string packagesRoot = "AssetsCompiled")
    {
        if (string.IsNullOrWhiteSpace(packagesRoot))
            throw new ArgumentException("Content packages root cannot be empty.", nameof(packagesRoot));
        _contentPackagesRoot = packagesRoot;
        _contentManifest = null;
        _contentPackage = null;
        _contentCatalogOnly = true;
        return this;
    }

    public Default2DRendererOptions UseHdr(
        ToneMappingSettings toneMapping,
        BloomSettings? bloom = null)
    {
        RenderViewEffects effects = RenderViewEffects.Hdr(toneMapping, bloom);
        _hdrEnabled = true;
        _toneMapping = effects.ToneMapping!.Value;
        _bloom = effects.Bloom;
        return this;
    }

    public Default2DRendererOptions EnableStencilMasking()
    {
        _stencilMaskingEnabled = true;
        return this;
    }

    public Default2DRendererOptions DisableSceneGui()
    {
        _sceneGuiEnabled = false;
        return this;
    }

    /// <summary>
    /// Presents the one rendered Camera view into multiple screen slots. This does
    /// not redraw the Scene or duplicate its post-processing chain.
    /// </summary>
    public Default2DRendererOptions UseSingleCameraViewports(
        Action<SingleCameraViewportLayoutBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_renderViews is not null)
            throw new InvalidOperationException(
                "UseSingleCameraViewports and UseRenderViews cannot be combined.");
        if (_viewports is not null)
            throw new InvalidOperationException("Single-camera Viewports are already configured.");
        var builder = new SingleCameraViewportLayoutBuilder();
        configure(builder);
        _viewports = builder.Build();
        return this;
    }

    /// <summary>Configures independently rendered Scene views with distinct Cameras.</summary>
    public Default2DRendererOptions UseRenderViews(Action<RenderViewLayoutBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_renderViews is not null)
            throw new InvalidOperationException("Render Views are already configured.");
        if (_viewports is not null)
            throw new InvalidOperationException(
                "UseRenderViews and UseSingleCameraViewports cannot be combined.");
        var builder = new RenderViewLayoutBuilder();
        configure(builder);
        _renderViews = builder.Build();
        return this;
    }

    /// <summary>
    /// Enables pixi-viewport-style interaction for the main Camera without requiring a second
    /// Render View. The configuration is also applied to main when UseRenderViews is enabled.
    /// </summary>
    public Default2DRendererOptions UseInteractiveViewport(
        Action<ViewportNavigationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_mainNavigation is not null)
            throw new InvalidOperationException("The main interactive Viewport is already configured.");
        var builder = new ViewportNavigationBuilder();
        configure(builder);
        _mainNavigation = builder.Build();
        return this;
    }

    public Default2DRendererOptions EnablePerformanceTelemetry(
        PerformanceTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_performanceTelemetry is not null)
            throw new InvalidOperationException("Performance telemetry is already enabled.");
        _performanceTelemetry = options;
        return this;
    }

    /// <summary>监测 AssetCompiler 的完整输出修订，并在 Step 与 Draw 之间原子替换内容。</summary>
    public Default2DRendererOptions EnableContentHotReload(ContentHotReloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_contentHotReload is not null)
            throw new InvalidOperationException("Content hot reload is already enabled.");
        _contentHotReload = options;
        return this;
    }

    /// <summary>注册从同一安全根目录读取的自定义 Sprite Shader 文件。</summary>
    public Default2DRendererOptions UseShaders(
        string root,
        params ShaderFileDefinition[] shaders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(shaders);
        if (_shaderRoot is not null)
            throw new InvalidOperationException("Shader files are already configured.");
        if (shaders.Length == 0)
            throw new ArgumentException("At least one Shader file definition is required.", nameof(shaders));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderFileDefinition shader in shaders)
        {
            ArgumentNullException.ThrowIfNull(shader);
            if (!names.Add(shader.Name))
                throw new ArgumentException($"Shader '{shader.Name}' is configured more than once.", nameof(shaders));
            _shaderFiles.Add(shader);
        }
        _shaderRoot = root;
        return this;
    }

    /// <summary>Load a strict shaders.json and configure its programs and materials together.</summary>
    public Default2DRendererOptions UseShaderAssets(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (_shaderRoot is not null)
            throw new InvalidOperationException("Shader files are already configured.");
        string fullPath = Path.GetFullPath(Path.IsPathRooted(manifestPath)
            ? manifestPath
            : Path.Combine(AppContext.BaseDirectory, manifestPath));
        LoadedShaderAssetManifest loaded = ShaderAssetManifestLoader.Load(fullPath);
        foreach (ShaderAssetDefinition shader in loaded.Manifest.Shaders)
        {
            _shaderFiles.Add(new ShaderFileDefinition(
                shader.Name,
                shader.VertexPath,
                shader.FragmentPath));
        }
        _shaderMaterials.AddRange(loaded.Manifest.Materials);
        _shaderRoot = loaded.RootDirectory;
        _shaderAssetManifestPath = loaded.ManifestPath;
        return this;
    }

    public Default2DRendererOptions EnableShaderHotReload(ShaderHotReloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_shaderHotReload is not null)
            throw new InvalidOperationException("Shader hot reload is already enabled.");
        _shaderHotReload = options;
        return this;
    }

    internal Default2DRendererPlan ToPlan() => new(
        _contentPackagesRoot,
        _contentManifest,
        _hdrEnabled,
        _toneMapping,
        _bloom,
        _stencilMaskingEnabled,
        _sceneGuiEnabled,
        _contentPackage,
        _performanceTelemetry,
        _contentHotReload,
        _contentCatalogOnly,
        _shaderRoot,
        _shaderFiles.ToArray(),
        _shaderHotReload,
        _shaderAssetManifestPath,
        _shaderMaterials.ToArray(),
        _viewports ?? SingleCameraViewportLayoutBuilder.Default,
        FreezeRenderViews(),
        _mainNavigation);

    private IReadOnlyList<RenderViewDefinition>? FreezeRenderViews()
    {
        if (_renderViews is null) return null;
        var result = _renderViews.ToArray();
        result[0] = RenderViewLayoutBuilder.WithEffects(
            result[0],
            _hdrEnabled
                ? RenderViewEffects.Hdr(_toneMapping, _bloom)
                : RenderViewEffects.Direct);
        if (_mainNavigation is not null)
        {
            if (result[0].Navigation is not null)
                throw new InvalidOperationException(
                    "Configure main Viewport navigation either with UseInteractiveViewport or " +
                    "UseRenderViews.ConfigureMain, not both.");
            result[0] = RenderViewLayoutBuilder.WithNavigation(result[0], _mainNavigation);
        }
        return Array.AsReadOnly(result);
    }
}

internal sealed record Default2DRendererPlan(
    string? ContentPackagesRoot,
    string? ContentManifest,
    bool HdrEnabled,
    ToneMappingSettings ToneMapping,
    BloomSettings? Bloom,
    bool StencilMaskingEnabled,
    bool SceneGuiEnabled,
    ContentPackageRef? ContentPackage = null,
    PerformanceTelemetryOptions? PerformanceTelemetry = null,
    ContentHotReloadOptions? ContentHotReload = null,
    bool ContentCatalogOnly = false,
    string? ShaderRoot = null,
    IReadOnlyList<ShaderFileDefinition>? ShaderFiles = null,
    ShaderHotReloadOptions? ShaderHotReload = null,
    string? ShaderAssetManifestPath = null,
    IReadOnlyList<MaterialAssetDefinition>? ShaderMaterials = null,
    IReadOnlyList<SingleCameraViewportDefinition>? Viewports = null,
    IReadOnlyList<RenderViewDefinition>? RenderViews = null,
    ViewportNavigationConfiguration? MainNavigation = null)
{
    public IReadOnlyList<SingleCameraViewportDefinition> ResolvedViewports =>
        Viewports ?? SingleCameraViewportLayoutBuilder.Default;
    public bool MultipleRenderViewsEnabled => RenderViews is { Count: > 1 };
    public RenderViewEffects MainEffects => HdrEnabled
        ? RenderViewEffects.Hdr(ToneMapping, Bloom)
        : RenderViewEffects.Direct;
    public bool AnyHdrEnabled => HdrEnabled ||
        RenderViews?.Any(view => view.Effects.IsHdr) == true;
    public bool AnyBloomEnabled => Bloom.HasValue ||
        RenderViews?.Any(view => view.Effects.Bloom.HasValue) == true;

    public void Validate()
    {
        if (ContentCatalogOnly)
        {
            if (ContentPackagesRoot is null || ContentManifest is not null || ContentPackage is not null)
                throw new InvalidOperationException(
                    "A content catalog requires only a package root; do not combine it with UseContent.");
        }
        else if ((ContentPackagesRoot is null) != (ContentManifest is null))
            throw new InvalidOperationException(
                "Content packages root and manifest must be configured together.");
        if (ContentPackage is { } package &&
            !StringComparer.Ordinal.Equals(package.Manifest, ContentManifest))
        {
            throw new InvalidOperationException(
                "The typed content package and configured manifest path must match.");
        }
        if (Bloom is not null && !HdrEnabled)
            throw new InvalidOperationException("The default Bloom preset requires HDR.");
        if (RenderViews is { Count: > 0 } renderViews &&
            (renderViews[0].Ref != RenderViewRef.Main || renderViews[0].Effects != MainEffects))
        {
            throw new InvalidOperationException(
                "The main Render View effect profile must match UseHdr configuration.");
        }
        if (ContentHotReload is not null && (ContentPackagesRoot is null || ContentCatalogOnly))
            throw new InvalidOperationException(
                "Content hot reload currently requires one eager package configured with UseContent.");
        if ((ShaderRoot is null) != (ShaderFiles is null or { Count: 0 }))
            throw new InvalidOperationException("Shader root and file definitions must be configured together.");
        if (ShaderHotReload is not null && ShaderFiles is not { Count: > 0 })
            throw new InvalidOperationException("Shader hot reload requires UseShaders.");
        if (ShaderAssetManifestPath is not null && ShaderMaterials is null)
            throw new InvalidOperationException(
                "Shader asset manifest and material definitions must be configured together.");
        if (ShaderMaterials is { Count: > 0 } && ShaderFiles is not { Count: > 0 })
            throw new InvalidOperationException(
                "Declarative materials require their Shader file definitions.");
        if (ResolvedViewports.Count == 0)
            throw new InvalidOperationException("At least one Viewport slot is required.");
    }
}
