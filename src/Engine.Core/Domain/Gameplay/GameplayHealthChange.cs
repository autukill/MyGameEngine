namespace GameEngine.Core.Domain.Gameplay;

/// <summary>A zero-allocation snapshot describing one clamped health mutation.</summary>
public readonly record struct GameplayHealthChange
{
    public float PreviousHealth { get; }
    public float CurrentHealth { get; }
    public float MaximumHealth { get; }

    public float Delta => CurrentHealth - PreviousHealth;
    public float AppliedAmount => MathF.Abs(Delta);
    public bool Changed => PreviousHealth != CurrentHealth;
    public bool IsDamage => Delta < 0f;
    public bool IsHealing => Delta > 0f;
    public bool BecameDepleted => PreviousHealth > 0f && CurrentHealth <= 0f;
    public bool BecameAlive => PreviousHealth <= 0f && CurrentHealth > 0f;
    public bool ReachedFull => PreviousHealth < MaximumHealth && CurrentHealth >= MaximumHealth;

    internal GameplayHealthChange(
        float previousHealth,
        float currentHealth,
        float maximumHealth)
    {
        PreviousHealth = previousHealth;
        CurrentHealth = currentHealth;
        MaximumHealth = maximumHealth;
    }
}
