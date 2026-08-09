namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;

/// <summary>
/// Reusable owner-local gameplay behavior. Behaviors are attached before an Instance enters a
/// Scene and receive the same lifecycle, pause, and time-domain scheduling as their owner.
/// </summary>
public abstract class GameplayBehavior
{
    private GameInstance? _owner;

    public GameInstance Owner => _owner ?? throw new InvalidOperationException(
        "Gameplay behavior is not attached to an owner.");

    /// <summary>The required owner type; generic behaviors override this automatically.</summary>
    public virtual Type RequiredOwnerType => typeof(GameInstance);

    public virtual void OnCreate() { }
    public virtual void OnBeginStep(double deltaTime) { }
    public virtual void OnStep(double deltaTime) { }
    public virtual void OnEndStep(double deltaTime) { }
    public virtual void OnDestroy() { }

    protected void DestroyOwner() => Owner.RequestDestroyFromBehavior();

    internal void Attach(GameInstance owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null)
            throw new InvalidOperationException(
                "A gameplay behavior instance can belong to only one owner.");
        if (!RequiredOwnerType.IsInstanceOfType(owner))
            throw new ArgumentException(
                $"Behavior '{GetType().Name}' requires owner type " +
                $"'{RequiredOwnerType.Name}', but received '{owner.GetType().Name}'.",
                nameof(owner));
        _owner = owner;
    }
}

/// <summary>Gameplay behavior with compile-time access to a specific GameInstance type.</summary>
public abstract class GameplayBehavior<TInstance> : GameplayBehavior
    where TInstance : GameInstance
{
    public sealed override Type RequiredOwnerType => typeof(TInstance);
    public new TInstance Owner => (TInstance)base.Owner;
}
