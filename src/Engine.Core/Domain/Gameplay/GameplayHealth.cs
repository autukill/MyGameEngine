namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Stores one clamped health value without defining armor, damage sources, death behavior,
/// presentation, or global combat services.
/// </summary>
public sealed class GameplayHealth
{
    public float MaximumHealth { get; }
    public float CurrentHealth { get; private set; }
    public float Normalized => CurrentHealth / MaximumHealth;
    public bool IsAlive => CurrentHealth > 0f;
    public bool IsDepleted => CurrentHealth <= 0f;
    public bool IsFull => CurrentHealth >= MaximumHealth;

    public GameplayHealth(float maximumHealth)
        : this(maximumHealth, maximumHealth)
    {
    }

    public GameplayHealth(float maximumHealth, float initialHealth)
    {
        if (!float.IsFinite(maximumHealth) || maximumHealth <= 0f)
            throw new ArgumentOutOfRangeException(
                nameof(maximumHealth), maximumHealth,
                "Maximum health must be finite and positive.");
        if (!float.IsFinite(initialHealth) || initialHealth < 0f || initialHealth > maximumHealth)
            throw new ArgumentOutOfRangeException(
                nameof(initialHealth), initialHealth,
                "Initial health must be finite and within [0, maximumHealth].");
        MaximumHealth = maximumHealth;
        CurrentHealth = initialHealth;
    }

    /// <summary>Applies finite non-negative damage and clamps at zero.</summary>
    public GameplayHealthChange ApplyDamage(float amount)
    {
        ValidateAmount(amount, nameof(amount));
        float previous = CurrentHealth;
        CurrentHealth = MathF.Max(0f, CurrentHealth - amount);
        return new GameplayHealthChange(previous, CurrentHealth, MaximumHealth);
    }

    /// <summary>Applies finite non-negative healing and clamps at maximum health.</summary>
    public GameplayHealthChange Heal(float amount)
    {
        ValidateAmount(amount, nameof(amount));
        float previous = CurrentHealth;
        CurrentHealth = MathF.Min(MaximumHealth, CurrentHealth + amount);
        return new GameplayHealthChange(previous, CurrentHealth, MaximumHealth);
    }

    /// <summary>Restores maximum health and reports the resulting change.</summary>
    public GameplayHealthChange Reset()
    {
        float previous = CurrentHealth;
        CurrentHealth = MaximumHealth;
        return new GameplayHealthChange(previous, CurrentHealth, MaximumHealth);
    }

    private static void ValidateAmount(float amount, string parameterName)
    {
        if (!float.IsFinite(amount) || amount < 0f)
            throw new ArgumentOutOfRangeException(
                parameterName, amount,
                "Health change amount must be finite and non-negative.");
    }
}
