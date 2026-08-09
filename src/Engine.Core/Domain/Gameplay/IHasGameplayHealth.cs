namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Opt-in capability for instances that expose mutable gameplay health.
/// Gameplay tags may identify damageable objects; this interface exposes how to damage them.
/// </summary>
public interface IHasGameplayHealth
{
    GameplayHealth Health { get; }
}
