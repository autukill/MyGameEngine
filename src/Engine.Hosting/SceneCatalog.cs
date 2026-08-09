namespace GameEngine.Hosting;

using GameEngine.Core.Domain.Gameplay;

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
    void Configure(Default2DGameContext context, ISceneActivation activation);
}

internal sealed class UntypedSceneDefinition(
    SceneRef scene,
    Action<Default2DGameContext> configure) : ISceneDefinition
{
    public SceneRef Scene { get; } = scene;
    public Type? ArgumentsType => null;

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
    Action<Default2DGameContext, TArgs> configure) : ISceneDefinition where TArgs : struct
{
    public SceneRef Scene { get; } = scene.Untyped;
    public Type? ArgumentsType => typeof(TArgs);

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
    private ISceneActivation? _pending;

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

    public bool IsSwitchPending => _pending is not null;

    public void SwitchTo(SceneRef scene) => Request(scene);

    public void SwitchTo<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct =>
        Request(scene, args);

    public void Request(SceneRef scene) => Queue(new UntypedSceneActivation(scene));

    public void Request<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct =>
        Queue(new TypedSceneActivation<TArgs>(scene, args));

    private void Queue(ISceneActivation activation)
    {
        Validate(activation);
        if (activation.Scene == Current) return;
        if (_pending is { } pending)
        {
            if (pending.HasSamePayload(activation)) return;
            throw new InvalidOperationException(
                $"Scene switch to '{pending.Scene.Name}' is already pending; cannot also request " +
                $"'{activation.Scene.Name}' with different arguments.");
        }
        _pending = activation;
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
        if (_pending is null)
        {
            activation = null!;
            return false;
        }
        activation = _pending;
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
}
