namespace GameEngine.Core.Domain.Input;

/// <summary>A stable logical two-dimensional gameplay axis name.</summary>
public readonly record struct InputAxis2DRef
{
    public string Name { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public InputAxis2DRef(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Input axis name cannot be empty.", nameof(name));
        Name = name;
    }

    public override string ToString() => Name ?? string.Empty;
}
