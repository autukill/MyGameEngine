namespace GameEngine.Features.TextRendering.Domain;

/// <summary>A stable logical reference to a registered font face.</summary>
public readonly record struct FontRef(string Name)
{
    public static FontRef Empty => default;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}
