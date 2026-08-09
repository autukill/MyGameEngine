namespace GameEngine.Core.Domain.Graphics;

/// <summary>A logical material reference that remains stable across shader program replacement.</summary>
public readonly record struct MaterialRef(string Name)
{
    public static MaterialRef Empty => default;

    public bool IsEmpty => string.IsNullOrEmpty(Name);

    public override string ToString() => Name;
}
