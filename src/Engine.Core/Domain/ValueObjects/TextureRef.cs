namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>A stable logical reference to a texture asset. It never owns a GPU handle.</summary>
public readonly record struct TextureRef(string Name)
{
    public static TextureRef Empty => default;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}
