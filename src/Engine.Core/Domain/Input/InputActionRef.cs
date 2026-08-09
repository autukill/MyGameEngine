namespace GameEngine.Core.Domain.Input;

/// <summary>A stable logical gameplay action name, independent from physical keys.</summary>
public readonly record struct InputActionRef
{
    public string Name { get; }
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public InputActionRef(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Input action name cannot be empty.", nameof(name));
        Name = name;
    }

    public override string ToString() => Name ?? string.Empty;
}
