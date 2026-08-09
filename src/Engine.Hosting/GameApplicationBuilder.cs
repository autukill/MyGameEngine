namespace GameEngine.Hosting;

using GameEngine.Core.Infrastructure.Windowing;

public sealed class GameApplicationBuilder
{
    private readonly EngineWindowOptions _windowOptions;
    private Default2DRendererOptions? _renderer;
    private string? _sceneName;
    private Action<Default2DGameContext>? _configureScene;

    internal GameApplicationBuilder(EngineWindowOptions windowOptions)
    {
        _windowOptions = windowOptions;
    }

    public GameApplicationBuilder UseDefault2DRenderer(
        Action<Default2DRendererOptions>? configure = null)
    {
        if (_renderer is not null)
            throw new InvalidOperationException("The default 2D renderer is already configured.");
        _renderer = new Default2DRendererOptions();
        configure?.Invoke(_renderer);
        return this;
    }

    public GameApplicationBuilder ConfigureScene(
        string sceneName,
        Action<Default2DGameContext> configure)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            throw new ArgumentException("Scene name cannot be empty.", nameof(sceneName));
        ArgumentNullException.ThrowIfNull(configure);
        if (_configureScene is not null)
            throw new InvalidOperationException("The initial Scene is already configured.");
        _sceneName = sceneName;
        _configureScene = configure;
        return this;
    }

    public GameApplication Build() => new(BuildPlan());

    internal GameApplicationPlan BuildPlan()
    {
        if (_renderer is null)
            throw new InvalidOperationException("Call UseDefault2DRenderer before Build.");
        if (_sceneName is null || _configureScene is null)
            throw new InvalidOperationException("Call ConfigureScene before Build.");
        var renderer = _renderer.ToPlan();
        renderer.Validate();
        return new GameApplicationPlan(
            _windowOptions,
            renderer,
            _sceneName,
            _configureScene);
    }
}

internal sealed record GameApplicationPlan(
    EngineWindowOptions WindowOptions,
    Default2DRendererPlan Renderer,
    string SceneName,
    Action<Default2DGameContext> ConfigureScene);
