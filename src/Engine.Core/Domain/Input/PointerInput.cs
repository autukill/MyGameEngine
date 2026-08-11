namespace GameEngine.Core.Domain.Input;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>Stable identifier for one mouse, touch, or pen contact.</summary>
public readonly record struct PointerId
{
    public static PointerId Mouse { get; } = new(0);

    public long Value { get; }

    public PointerId(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum PointerKind
{
    Mouse = 0,
    Touch = 1,
    Pen = 2,
    Unknown = 3,
}

/// <summary>
/// One raw screen-space pointer sample. Providers expose currently known contacts; a contact may
/// remain for its release frame with <see cref="IsDown"/> false, or disappear immediately.
/// Consumers must handle both release shapes.
/// </summary>
public readonly record struct PointerContact
{
    public PointerId Id { get; }
    public PointerKind Kind { get; }
    public Vector2D Position { get; }
    public bool IsDown { get; }
    public bool IsPrimary { get; }

    public PointerContact(
        PointerId id,
        PointerKind kind,
        Vector2D position,
        bool isDown,
        bool isPrimary = false)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        Id = id;
        Kind = kind;
        Position = position;
        IsDown = isDown;
        IsPrimary = isPrimary;
    }
}
