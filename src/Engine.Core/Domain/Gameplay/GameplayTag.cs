namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Stable case-sensitive gameplay identity used for cross-cutting roles such as enemy,
/// damageable, or pickup without coupling queries to one inheritance hierarchy.
/// </summary>
public readonly record struct GameplayTag
{
    public string Name { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public GameplayTag(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Gameplay tag name cannot be empty.", nameof(name));
        Name = name;
    }

    public override string ToString() => Name ?? string.Empty;
}
