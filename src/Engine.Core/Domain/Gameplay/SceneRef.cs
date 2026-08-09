namespace GameEngine.Core.Domain.Gameplay;

/// <summary>A stable logical reference to a declaratively registered Scene.</summary>
public readonly record struct SceneRef
{
    public string Name { get; }

    public SceneRef(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

/// <summary>
/// A stable logical Scene reference whose activation argument type is checked at compile time.
/// Scene arguments are copied when a switch is requested, so value-type payloads form a stable
/// frame-boundary snapshot.
/// </summary>
public readonly record struct SceneRef<TArgs> where TArgs : struct
{
    public string Name { get; }

    public SceneRef(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public SceneRef Untyped => new(Name);

    public override string ToString() => Name ?? string.Empty;
}
