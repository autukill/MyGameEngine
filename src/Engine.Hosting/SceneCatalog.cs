namespace GameEngine.Hosting;

using GameEngine.Core.Domain.Gameplay;

public sealed record SceneDefinition(
    SceneRef Scene,
    Action<Default2DGameContext> Configure);

/// <summary>
/// Runtime view of the declarative Scene catalog. Switch requests are committed by Hosting after
/// the current Step, never from inside an Instance callback.
/// </summary>
public sealed class SceneNavigator
{
    private readonly IReadOnlyDictionary<string, SceneDefinition> _definitions;
    private SceneRef? _pending;

    internal SceneNavigator(
        IReadOnlyDictionary<string, SceneDefinition> definitions,
        SceneRef initial)
    {
        _definitions = definitions;
        Current = initial;
    }

    public SceneRef Current { get; private set; }

    public IReadOnlyList<SceneRef> Available =>
        _definitions.Values.Select(definition => definition.Scene).ToArray();

    public bool IsSwitchPending => _pending is not null;

    public void SwitchTo(SceneRef scene)
    {
        if (scene.IsEmpty)
            throw new ArgumentException("Scene reference cannot be empty.", nameof(scene));
        if (!_definitions.ContainsKey(scene.Name))
            throw new KeyNotFoundException($"Scene '{scene.Name}' is not registered.");
        if (scene == Current) return;
        if (_pending is { } pending)
        {
            if (pending == scene) return;
            throw new InvalidOperationException(
                $"Scene switch to '{pending.Name}' is already pending; cannot also request '{scene.Name}'.");
        }
        _pending = scene;
    }

    internal bool TryTakePending(out SceneRef scene)
    {
        if (_pending is not { } pending)
        {
            scene = default;
            return false;
        }
        _pending = null;
        scene = pending;
        return true;
    }

    internal SceneDefinition GetDefinition(SceneRef scene) => _definitions[scene.Name];

    internal void Commit(SceneRef scene) => Current = scene;
}
