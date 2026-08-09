namespace GameEngine.Core.Domain.Gameplay;

/// <summary>A stable logical name for one lightweight per-instance alarm.</summary>
public readonly record struct AlarmId
{
    public string Name { get; }

    public AlarmId(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public bool IsEmpty => string.IsNullOrEmpty(Name);

    public override string ToString() => Name;
}
