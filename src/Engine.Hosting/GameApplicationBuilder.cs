namespace GameEngine.Hosting;

using System.Collections.ObjectModel;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Infrastructure.Windowing;

public sealed class GameApplicationBuilder
{
    private readonly EngineWindowOptions _windowOptions;
    private Default2DRendererOptions? _renderer;
    private readonly Dictionary<string, ISceneDefinition> _scenes = new(StringComparer.Ordinal);
    private readonly InstanceFactory _instances = new();
    private ISceneActivation? _initialScene;
    private bool _instancesConfigured;
    private InputMap _inputMap = InputMap.Empty;
    private bool _inputConfigured;

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
        _initialScene = new UntypedSceneActivation(scene);
        return this;
    }

    public GameApplicationBuilder AddScene(
        SceneRef scene,
        Action<Default2DGameContext> configure)
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        ArgumentNullException.ThrowIfNull(configure);
        if (!_scenes.TryAdd(scene.Name, new UntypedSceneDefinition(scene, configure)))
            throw new ArgumentException($"Scene '{scene.Name}' is already registered.", nameof(scene));
        _initialScene ??= new UntypedSceneActivation(scene);
        return this;
    }

    public GameApplicationBuilder AddScene<TArgs>(
        SceneRef<TArgs> scene,
        Action<Default2DGameContext, TArgs> configure) where TArgs : struct
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        ArgumentNullException.ThrowIfNull(configure);
        if (!_scenes.TryAdd(scene.Name, new TypedSceneDefinition<TArgs>(scene, configure)))
            throw new ArgumentException($"Scene '{scene.Name}' is already registered.", nameof(scene));
        _initialScene ??= new UntypedSceneActivation(scene.Untyped);
        return this;
    }

    public GameApplicationBuilder StartScene(SceneRef scene)
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        _initialScene = new UntypedSceneActivation(scene);
        return this;
    }

    public GameApplicationBuilder StartScene<TArgs>(
        SceneRef<TArgs> scene,
        in TArgs args) where TArgs : struct
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        _initialScene = new TypedSceneActivation<TArgs>(scene, args);
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

    public GameApplicationBuilder ConfigureInput(Action<InputMapBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_inputConfigured)
            throw new InvalidOperationException("Input bindings are already configured.");
        var builder = new InputMapBuilder();
        configure(builder);
        InputMap inputMap = builder.Build();
        if (inputMap.IsEmpty)
            throw new InvalidOperationException("ConfigureInput requires at least one binding.");
        _inputMap = inputMap;
        _inputConfigured = true;
        return this;
    }

    public GameApplication Build() => new(BuildPlan());

    internal GameApplicationPlan BuildPlan()
    {
        if (_renderer is null)
            throw new InvalidOperationException("Call UseDefault2DRenderer before Build.");
        if (_initialScene is not { } initial ||
            !_scenes.TryGetValue(initial.Scene.Name, out ISceneDefinition? initialDefinition))
        {
            throw new InvalidOperationException(
                "Register the initial Scene with ConfigureScene/AddScene before Build.");
        }
        if (initialDefinition.ArgumentsType != initial.ArgumentsType)
        {
            string expected = initialDefinition.ArgumentsType?.Name ?? "no arguments";
            throw new InvalidOperationException(
                $"Initial Scene '{initial.Scene.Name}' expects {expected}. " +
                "Select it with the matching StartScene overload.");
        }
        var renderer = _renderer.ToPlan();
        renderer.Validate();
        EngineWindowOptions windowOptions = renderer.PerformanceTelemetry is not null &&
                                            _windowOptions.FrameStatistics is null
            ? _windowOptions.WithFrameStatistics()
            : _windowOptions;
        var scenes = new ReadOnlyDictionary<string, ISceneDefinition>(
            new Dictionary<string, ISceneDefinition>(_scenes, StringComparer.Ordinal));
        return new GameApplicationPlan(
            windowOptions,
            renderer,
            initial,
            scenes,
            _instances.Build(),
            _inputMap);
    }
}

internal sealed record GameApplicationPlan(
    EngineWindowOptions WindowOptions,
    Default2DRendererPlan Renderer,
    ISceneActivation InitialSceneActivation,
    IReadOnlyDictionary<string, ISceneDefinition> Scenes,
    IInstanceFactory Instances,
    InputMap InputMap)
{
    public SceneRef InitialScene => InitialSceneActivation.Scene;
    public string SceneName => InitialScene.Name;
    public Action<Default2DGameContext> ConfigureScene => context =>
        Scenes[InitialScene.Name].Configure(context, InitialSceneActivation);
}
