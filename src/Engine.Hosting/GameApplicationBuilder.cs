namespace GameEngine.Hosting;

using System.Collections.ObjectModel;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.Replay.Application;

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
    private LogicalInputRecorder? _inputRecorder;
    private LogicalInputRecording? _inputPlayback;
    private GameplayStateRecorder? _stateRecorder;
    private GameplayStateVerifier? _stateVerifier;
    private bool _closeOnReplayCompletion;
    private AudioHostingOptions? _audio;

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

    /// <summary>Enables logical audio playback and a real OpenAL device with silent fallback.</summary>
    public GameApplicationBuilder UseAudio(AudioHostingOptions? options = null)
    {
        if (_audio is not null)
            throw new InvalidOperationException("Audio is already configured.");
        _audio = options ?? new AudioHostingOptions();
        _audio.Validate();
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

    /// <summary>Records one logical Action/Axis snapshot for every fixed simulation Step.</summary>
    public GameApplicationBuilder RecordLogicalInput(LogicalInputRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        RequireInputReplayNotConfigured();
        _inputRecorder = recorder;
        return this;
    }

    /// <summary>Replays logical Action/Axis snapshots without reading physical gameplay input.</summary>
    public GameApplicationBuilder ReplayLogicalInput(LogicalInputRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        RequireInputReplayNotConfigured();
        _inputPlayback = recording;
        return this;
    }

    /// <summary>Captures one stable gameplay state hash after every committed simulation Step.</summary>
    public GameApplicationBuilder RecordGameplayState(GameplayStateRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        RequireGameplayStateDiagnosticsNotConfigured();
        _stateRecorder = recorder;
        return this;
    }

    /// <summary>Fails at the first simulation Step that differs from a recorded state trace.</summary>
    public GameApplicationBuilder VerifyGameplayState(GameplayStateVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        RequireGameplayStateDiagnosticsNotConfigured();
        _stateVerifier = verifier;
        return this;
    }

    /// <summary>
    /// Records logical input and deterministic gameplay-state hashes into one session. Save the
    /// session after GameApplication.Run returns.
    /// </summary>
    public GameApplicationBuilder UseReplayRecording(ReplaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.RequireMode(ReplaySessionMode.Recording);
        RequireInputReplayNotConfigured();
        RequireGameplayStateDiagnosticsNotConfigured();
        _inputRecorder = session.InputRecorder!;
        _stateRecorder = session.StateRecorder!;
        return this;
    }

    /// <summary>
    /// Replays logical input and verifies state hashes. By default the application closes after
    /// the final verified Tick, making unattended regression playback straightforward.
    /// </summary>
    public GameApplicationBuilder UseReplayPlayback(
        ReplaySession session,
        bool closeWhenComplete = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.RequireMode(ReplaySessionMode.Playback);
        RequireInputReplayNotConfigured();
        RequireGameplayStateDiagnosticsNotConfigured();
        _inputPlayback = session.Bundle!.Input;
        _stateVerifier = new GameplayStateVerifier(session.Bundle.GameplayState);
        _closeOnReplayCompletion = closeWhenComplete;
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
        if (_inputRecorder is not null || _inputPlayback is not null)
        {
            if (_inputMap.IsEmpty)
                throw new InvalidOperationException(
                    "Logical input recording and playback require ConfigureInput.");
            if (windowOptions.FixedDeltaTime is not { } fixedDeltaTime ||
                !double.IsFinite(fixedDeltaTime) || fixedDeltaTime <= 0d)
            {
                throw new InvalidOperationException(
                    "Logical input recording and playback require a fixed delta. " +
                    "Configure EngineWindowOptions with WithFixedUpdateRate.");
            }
            if (_inputRecorder is { FrameCount: > 0 })
                throw new InvalidOperationException(
                    "Hosting requires a fresh LogicalInputRecorder with no captured frames.");
            if (_inputPlayback is { FrameCount: 0 })
                throw new InvalidOperationException(
                    "Logical input playback requires at least one recorded frame.");
            if (_inputPlayback is { FirstStepIndex: not 1 })
                throw new InvalidOperationException(
                    "Hosting logical input playback must begin at simulation Step 1.");
            if (_inputPlayback is { FixedDeltaSeconds: null })
                throw new InvalidOperationException(
                    "Hosting logical input playback requires recorded fixed delta metadata.");
            if (_inputPlayback is { FixedDeltaSeconds: { } recordedDelta } &&
                BitConverter.DoubleToInt64Bits(recordedDelta) !=
                BitConverter.DoubleToInt64Bits(fixedDeltaTime))
            {
                throw new InvalidOperationException(
                    $"Logical input playback fixed delta {recordedDelta:R} does not match " +
                    $"the configured value {fixedDeltaTime:R}.");
            }
            _inputPlayback?.ValidateAgainst(_inputMap);
        }
        if (_stateRecorder is not null || _stateVerifier is not null)
        {
            if (windowOptions.FixedDeltaTime is not { } fixedDeltaTime ||
                !double.IsFinite(fixedDeltaTime) || fixedDeltaTime <= 0d)
            {
                throw new InvalidOperationException(
                    "Gameplay state recording and verification require a fixed delta. " +
                    "Configure EngineWindowOptions with WithFixedUpdateRate.");
            }
            if (_stateRecorder is { SnapshotCount: > 0 })
                throw new InvalidOperationException(
                    "Hosting requires a fresh GameplayStateRecorder with no snapshots.");
            if (_stateRecorder is { FixedDeltaSeconds: { } preparedDelta } &&
                BitConverter.DoubleToInt64Bits(preparedDelta) !=
                BitConverter.DoubleToInt64Bits(fixedDeltaTime))
            {
                throw new InvalidOperationException(
                    "The GameplayStateRecorder was prepared with a different fixed delta.");
            }
            if (_stateVerifier is { CurrentStepIndex: not 0 })
                throw new InvalidOperationException(
                    "Hosting requires a fresh GameplayStateVerifier.");
            if (_stateVerifier is { Recording.SnapshotCount: 0 })
                throw new InvalidOperationException(
                    "Gameplay state verification requires at least one baseline snapshot.");
            if (_stateVerifier is { Recording.FirstStepIndex: not 1 })
                throw new InvalidOperationException(
                    "Hosting gameplay state verification must begin at simulation Step 1.");
            if (_stateVerifier is { } verifier &&
                BitConverter.DoubleToInt64Bits(verifier.Recording.FixedDeltaSeconds) !=
                BitConverter.DoubleToInt64Bits(fixedDeltaTime))
            {
                throw new InvalidOperationException(
                    "Gameplay state baseline fixed delta does not match the configured value.");
            }
        }
        var scenes = new ReadOnlyDictionary<string, ISceneDefinition>(
            new Dictionary<string, ISceneDefinition>(_scenes, StringComparer.Ordinal));
        return new GameApplicationPlan(
            windowOptions,
            renderer,
            initial,
            scenes,
            _instances.Build(),
            _inputMap,
            _inputRecorder,
            _inputPlayback,
            _stateRecorder,
            _stateVerifier,
            _closeOnReplayCompletion,
            _audio);
    }

    private void RequireInputReplayNotConfigured()
    {
        if (_inputRecorder is not null || _inputPlayback is not null)
            throw new InvalidOperationException(
                "Logical input recording or playback is already configured.");
    }

    private void RequireGameplayStateDiagnosticsNotConfigured()
    {
        if (_stateRecorder is not null || _stateVerifier is not null)
            throw new InvalidOperationException(
                "Gameplay state recording or verification is already configured.");
    }
}

internal sealed record GameApplicationPlan(
    EngineWindowOptions WindowOptions,
    Default2DRendererPlan Renderer,
    ISceneActivation InitialSceneActivation,
    IReadOnlyDictionary<string, ISceneDefinition> Scenes,
    IInstanceFactory Instances,
    InputMap InputMap,
    LogicalInputRecorder? InputRecorder,
    LogicalInputRecording? InputPlayback,
    GameplayStateRecorder? StateRecorder,
    GameplayStateVerifier? StateVerifier,
    bool CloseOnReplayCompletion,
    AudioHostingOptions? Audio)
{
    public SceneRef InitialScene => InitialSceneActivation.Scene;
    public string SceneName => InitialScene.Name;
    public Action<Default2DGameContext> ConfigureScene => context =>
        Scenes[InitialScene.Name].Configure(context, InitialSceneActivation);
}
