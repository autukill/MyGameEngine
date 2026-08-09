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
