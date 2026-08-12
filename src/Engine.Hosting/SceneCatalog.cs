namespace GameEngine.Hosting;

using System.Numerics;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Features.ContentAssets.Domain;

internal interface ISceneActivation
{
    SceneRef Scene { get; }
    Type? ArgumentsType { get; }
    bool HasSamePayload(ISceneActivation other);
}

internal sealed class UntypedSceneActivation(SceneRef scene) : ISceneActivation
{
    public SceneRef Scene { get; } = scene;
    public Type? ArgumentsType => null;

    public bool HasSamePayload(ISceneActivation other) =>
        other is UntypedSceneActivation && other.Scene == Scene;
}

internal sealed class TypedSceneActivation<TArgs>(SceneRef<TArgs> scene, in TArgs arguments)
    : ISceneActivation where TArgs : struct
{
    public SceneRef Scene { get; } = scene.Untyped;
    public Type? ArgumentsType => typeof(TArgs);
    public TArgs Arguments { get; } = arguments;

    public bool HasSamePayload(ISceneActivation other) =>
        other is TypedSceneActivation<TArgs> typed &&
        typed.Scene == Scene &&
        EqualityComparer<TArgs>.Default.Equals(typed.Arguments, Arguments);
}

internal interface ISceneDefinition
{
    SceneRef Scene { get; }
    Type? ArgumentsType { get; }
    ContentPackageRef? ContentPackage { get; }
    IReadOnlyDictionary<string, SceneRenderViewDefinition>? Views { get; }
    void Configure(Default2DGameContext context, ISceneActivation activation);
}

internal sealed class UntypedSceneDefinition(
    SceneRef scene,
    ContentPackageRef? contentPackage,
    IReadOnlyDictionary<string, SceneRenderViewDefinition>? views,
    Action<Default2DGameContext> configure) : ISceneDefinition
{
    public SceneRef Scene { get; } = scene;
    public Type? ArgumentsType => null;
    public ContentPackageRef? ContentPackage { get; } = contentPackage;
    public IReadOnlyDictionary<string, SceneRenderViewDefinition>? Views { get; } = views;

    public void Configure(Default2DGameContext context, ISceneActivation activation)
    {
        if (activation is not UntypedSceneActivation || activation.Scene != Scene)
            throw new InvalidOperationException(
                $"Scene '{Scene.Name}' was activated with incompatible arguments.");
        configure(context);
    }
}

internal sealed class TypedSceneDefinition<TArgs>(
    SceneRef<TArgs> scene,
    ContentPackageRef? contentPackage,
    IReadOnlyDictionary<string, SceneRenderViewDefinition>? views,
    Action<Default2DGameContext, TArgs> configure) : ISceneDefinition where TArgs : struct
{
    public SceneRef Scene { get; } = scene.Untyped;
    public Type? ArgumentsType => typeof(TArgs);
    public ContentPackageRef? ContentPackage { get; } = contentPackage;
    public IReadOnlyDictionary<string, SceneRenderViewDefinition>? Views { get; } = views;

    public void Configure(Default2DGameContext context, ISceneActivation activation)
    {
        if (activation is not TypedSceneActivation<TArgs> typed || typed.Scene != Scene)
            throw new InvalidOperationException(
                $"Scene '{Scene.Name}' was activated with incompatible arguments.");
        configure(context, typed.Arguments);
    }
}

/// <summary>
/// Runtime view of the declarative Scene catalog. Switch requests are committed by Hosting after
/// the current Step, never from inside an Instance callback.
/// </summary>
public sealed class SceneNavigator : ISceneSwitchRequester
{
    private readonly IReadOnlyDictionary<string, ISceneDefinition> _definitions;
    private SceneSwitchRequest? _pending;
    private SceneSwitchRequest? _active;
    private SceneTransitionPhase _phase;
    private double _phaseElapsed;
    private bool _readyTaken;

    internal SceneNavigator(
        IReadOnlyDictionary<string, ISceneDefinition> definitions,
        ISceneActivation initial)
    {
        _definitions = definitions;
        Validate(initial);
        Current = initial.Scene;
    }

    internal SceneNavigator(
        IReadOnlyDictionary<string, ISceneDefinition> definitions,
        SceneRef initial)
        : this(definitions, new UntypedSceneActivation(initial)) { }

    public SceneRef Current { get; private set; }

    public IReadOnlyList<SceneRef> Available =>
        _definitions.Values.Select(definition => definition.Scene).ToArray();

    public bool IsSwitchPending => _pending is not null ||
        _phase is SceneTransitionPhase.FadingOut or SceneTransitionPhase.Switching;
    public bool IsTransitioning => _phase != SceneTransitionPhase.Idle;
    public SceneTransitionSnapshot Transition => CaptureTransition();
    public SceneTransitionFailure? LastTransitionFailure { get; private set; }

    public void SwitchTo(SceneRef scene) => Request(scene);

    public void SwitchTo(SceneRef scene, SceneTransitionOptions transition) =>
        Request(scene, transition);

    public void SwitchTo<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct =>
        Request(scene, args);

    public void SwitchTo<TArgs>(
        SceneRef<TArgs> scene,
        in TArgs args,
        SceneTransitionOptions transition) where TArgs : struct =>
        Request(scene, args, transition);

    public void Request(SceneRef scene) => Queue(new UntypedSceneActivation(scene), null);

    public void Request(SceneRef scene, SceneTransitionOptions transition) =>
        Queue(new UntypedSceneActivation(scene), CheckedTransition(transition));

    public void Request<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct =>
        Queue(new TypedSceneActivation<TArgs>(scene, args), null);

    public void Request<TArgs>(
        SceneRef<TArgs> scene,
        in TArgs args,
        SceneTransitionOptions transition) where TArgs : struct =>
        Queue(new TypedSceneActivation<TArgs>(scene, args), CheckedTransition(transition));

    private void Queue(ISceneActivation activation, SceneTransitionOptions? transition)
    {
        Validate(activation);
        var request = new SceneSwitchRequest(activation, transition);
        if (_phase == SceneTransitionPhase.FadingIn && activation.Scene == Current) return;
        if (_active is { } active)
        {
            if (active.HasSameRequest(request)) return;
            throw ConflictingRequest(active, request);
        }
        if (_pending is { } pending)
        {
            if (pending.HasSameRequest(request)) return;
            throw ConflictingRequest(pending, request);
        }
        if (activation.Scene == Current) return;
        LastTransitionFailure = null;
        _pending = request;
    }

    private void Validate(ISceneActivation activation)
    {
        if (activation.Scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(activation));
        if (!_definitions.TryGetValue(activation.Scene.Name, out ISceneDefinition? definition))
            throw new KeyNotFoundException($"Scene '{activation.Scene.Name}' is not registered.");
        if (definition.ArgumentsType != activation.ArgumentsType)
        {
            string expected = definition.ArgumentsType?.Name ?? "no arguments";
            string actual = activation.ArgumentsType?.Name ?? "no arguments";
            throw new InvalidOperationException(
                $"Scene '{activation.Scene.Name}' expects {expected}, but received {actual}.");
        }
    }

    internal bool TryTakePending(out ISceneActivation activation)
    {
        if (_pending is not { Transition: null } pending)
        {
            activation = null!;
            return false;
        }
        activation = pending.Activation;
        _pending = null;
        return true;
    }

    internal bool TryTakePending(out SceneRef scene)
    {
        if (!TryTakePending(out ISceneActivation activation))
        {
            scene = default;
            return false;
        }
        scene = activation.Scene;
        return true;
    }

    internal ISceneDefinition GetDefinition(SceneRef scene) => _definitions[scene.Name];

    internal void Commit(SceneRef scene) => Current = scene;

    internal void BeginPendingTransition()
    {
        if (_active is not null || _pending is not { Transition: { } options } pending) return;
        _active = pending;
        _pending = null;
        _phase = options.FadeOutDuration == 0d
            ? SceneTransitionPhase.Switching
            : SceneTransitionPhase.FadingOut;
        _phaseElapsed = 0d;
        _readyTaken = false;
    }

    internal void AdvanceTransition(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (_active?.Transition is not { } options) return;

        switch (_phase)
        {
            case SceneTransitionPhase.FadingOut:
                _phaseElapsed += deltaTime;
                if (_phaseElapsed >= options.FadeOutDuration)
                {
                    _phase = SceneTransitionPhase.Switching;
                    _phaseElapsed = options.FadeOutDuration;
                }
                break;
            case SceneTransitionPhase.FadingIn:
                _phaseElapsed += deltaTime;
                if (_phaseElapsed >= options.FadeInDuration)
                    FinishTransition();
                break;
        }
    }

    internal bool TryTakeReady(out SceneSwitchRequest request)
    {
        if (_pending is { Transition: null } immediate)
        {
            _pending = null;
            request = immediate;
            return true;
        }
        if (_active is { } active &&
            _phase == SceneTransitionPhase.Switching &&
            !_readyTaken)
        {
            _readyTaken = true;
            request = active;
            return true;
        }
        request = null!;
        return false;
    }

    internal void CompleteSwitch(SceneSwitchRequest request)
    {
        Current = request.Activation.Scene;
        if (request.Transition is not { } options)
            return;
        RequireActive(request);
        _readyTaken = false;
        _phaseElapsed = 0d;
        if (options.FadeInDuration == 0d) FinishTransition();
        else _phase = SceneTransitionPhase.FadingIn;
    }

    internal void AbortPreCommit(SceneSwitchRequest request, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        RequireActive(request);
        LastTransitionFailure = new SceneTransitionFailure(
            Current,
            request.Activation.Scene,
            exception);
        _readyTaken = false;
        _phaseElapsed = 0d;
        if (request.Transition!.Value.FadeInDuration == 0d) FinishTransition();
        else _phase = SceneTransitionPhase.FadingIn;
    }

    private SceneTransitionSnapshot CaptureTransition()
    {
        if (_active?.Transition is not { } options)
            return new SceneTransitionSnapshot(
                SceneTransitionPhase.Idle,
                default,
                0f,
                Vector4.Zero,
                false);
        float opacity = _phase switch
        {
            SceneTransitionPhase.FadingOut => options.FadeOutDuration == 0d
                ? 1f
                : (float)Math.Clamp(_phaseElapsed / options.FadeOutDuration, 0d, 1d),
            SceneTransitionPhase.Switching => 1f,
            SceneTransitionPhase.FadingIn => options.FadeInDuration == 0d
                ? 0f
                : 1f - (float)Math.Clamp(_phaseElapsed / options.FadeInDuration, 0d, 1d),
            _ => 0f
        };
        return new SceneTransitionSnapshot(
            _phase,
            _active.Activation.Scene,
            opacity,
            options.Color,
            options.BlockInput);
    }

    private void FinishTransition()
    {
        _active = null;
        _phase = SceneTransitionPhase.Idle;
        _phaseElapsed = 0d;
        _readyTaken = false;
    }

    private void RequireActive(SceneSwitchRequest request)
    {
        if (!ReferenceEquals(_active, request) || _phase != SceneTransitionPhase.Switching)
            throw new InvalidOperationException("Scene transition request is not ready to commit.");
    }

    private static SceneTransitionOptions CheckedTransition(SceneTransitionOptions transition) =>
        transition.IsInitialized
            ? transition
            : throw new ArgumentException(
                "Scene transition options must be explicitly initialized.",
                nameof(transition));

    private static InvalidOperationException ConflictingRequest(
        SceneSwitchRequest pending,
        SceneSwitchRequest next) =>
        new(
            $"Scene switch to '{pending.Activation.Scene.Name}' is already pending; cannot also " +
            $"request '{next.Activation.Scene.Name}' with different arguments or transition options.");
}
