namespace GameEngine.Hosting;

using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ToneMapping.Domain;

/// <summary>默认 2D 渲染预设；只有显式启用的可选 Feature 才创建 GPU 资源。</summary>
public sealed class Default2DRendererOptions
{
    private string? _contentPackagesRoot;
    private string? _contentManifest;
    private ContentPackageRef? _contentPackage;
    private bool _hdrEnabled;
    private ToneMappingSettings _toneMapping = ToneMappingSettings.Default;
    private BloomSettings? _bloom;
    private bool _stencilMaskingEnabled;
    private bool _sceneGuiEnabled = true;
    private PerformanceTelemetryOptions? _performanceTelemetry;

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
        return this;
    }

    public Default2DRendererOptions UseHdr(
        ToneMappingSettings toneMapping,
        BloomSettings? bloom = null)
    {
        _hdrEnabled = true;
        _toneMapping = toneMapping;
        _bloom = bloom;
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

    public Default2DRendererOptions EnablePerformanceTelemetry(
        PerformanceTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_performanceTelemetry is not null)
            throw new InvalidOperationException("Performance telemetry is already enabled.");
        _performanceTelemetry = options;
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
        _performanceTelemetry);
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
    PerformanceTelemetryOptions? PerformanceTelemetry = null)
{
    public void Validate()
    {
        if ((ContentPackagesRoot is null) != (ContentManifest is null))
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
    }
}
