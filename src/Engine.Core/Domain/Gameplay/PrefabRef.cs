namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;

/// <summary>A type-safe logical reference to an Instance factory registration.</summary>
public readonly record struct PrefabRef<T> where T : GameInstance
{
    public string Name { get; }

    public PrefabRef(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}
