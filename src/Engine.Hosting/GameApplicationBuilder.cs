namespace GameEngine.Hosting;

using System.Collections.ObjectModel;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Infrastructure.Windowing;

public sealed class GameApplicationBuilder
{
    private readonly EngineWindowOptions _windowOptions;
    private Default2DRendererOptions? _renderer;
    private readonly Dictionary<string, SceneDefinition> _scenes = new(StringComparer.Ordinal);
    private readonly InstanceFactory _instances = new();
    private SceneRef? _initialScene;
    private bool _instancesConfigured;

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
        if (_initialScene is not null || _scenes.Count > 0)
            throw new InvalidOperationException("The initial Scene is already configured.");
        SceneRef scene = new(sceneName);
        AddScene(scene, configure);
        _initialScene = scene;
        return this;
    }

    public GameApplicationBuilder AddScene(
        SceneRef scene,
        Action<Default2DGameContext> configure)
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        ArgumentNullException.ThrowIfNull(configure);
        if (!_scenes.TryAdd(scene.Name, new SceneDefinition(scene, configure)))
            throw new ArgumentException($"Scene '{scene.Name}' is already registered.", nameof(scene));
        _initialScene ??= scene;
        return this;
    }

    public GameApplicationBuilder StartScene(SceneRef scene)
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        _initialScene = scene;
        return this;
    }

    public GameApplicationBuilder ConfigureInstances(Action<InstanceFactory> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_instancesConfigured)
            throw new InvalidOperationException("Instance factories are already configured.");
        configure(_instances);
        _instancesConfigured = true;
        return this;
    }

    public GameApplication Build() => new(BuildPlan());

    internal GameApplicationPlan BuildPlan()
    {
        if (_renderer is null)
            throw new InvalidOperationException("Call UseDefault2DRenderer before Build.");
        if (_initialScene is not { } initial ||
            !_scenes.ContainsKey(initial.Name))
        {
            throw new InvalidOperationException(
                "Register the initial Scene with ConfigureScene/AddScene before Build.");
        }
        var renderer = _renderer.ToPlan();
        renderer.Validate();
        EngineWindowOptions windowOptions = renderer.PerformanceTelemetry is not null &&
                                            _windowOptions.FrameStatistics is null
            ? _windowOptions.WithFrameStatistics()
            : _windowOptions;
        var scenes = new ReadOnlyDictionary<string, SceneDefinition>(
            new Dictionary<string, SceneDefinition>(_scenes, StringComparer.Ordinal));
        return new GameApplicationPlan(
            windowOptions,
            renderer,
            initial,
            scenes,
            _instances.Build());
    }
}

internal sealed record GameApplicationPlan(
    EngineWindowOptions WindowOptions,
    Default2DRendererPlan Renderer,
    SceneRef InitialScene,
    IReadOnlyDictionary<string, SceneDefinition> Scenes,
    IInstanceFactory Instances)
{
    public string SceneName => InitialScene.Name;
    public Action<Default2DGameContext> ConfigureScene =>
        Scenes[InitialScene.Name].Configure;
}
