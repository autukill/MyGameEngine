namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public readonly record struct PrefabSpawnContext(Vector2D Position);

public interface IInstanceFactory
{
    T Create<T>(PrefabRef<T> prefab, in PrefabSpawnContext context)
        where T : GameInstance;
}

/// <summary>
/// Composition-root registry for pure gameplay Instance factories. Registrations are frozen when
/// Build is called so runtime gameplay cannot mutate the prefab catalog.
/// </summary>
public sealed class InstanceFactory : IInstanceFactory
{
    private readonly Dictionary<string, IRegistration> _registrations =
        new(StringComparer.Ordinal);
    private bool _frozen;

    public int Count => _registrations.Count;

    public InstanceFactory Register<T>(
        PrefabRef<T> prefab,
        Func<PrefabSpawnContext, T> create)
        where T : GameInstance
    {
        if (_frozen)
            throw new InvalidOperationException("The Instance factory catalog is frozen.");
        if (prefab.IsEmpty)
            throw new ArgumentException("Prefab reference cannot be empty.", nameof(prefab));
        ArgumentNullException.ThrowIfNull(create);
        if (!_registrations.TryAdd(prefab.Name, new Registration<T>(create)))
            throw new ArgumentException(
                $"Prefab '{prefab.Name}' is already registered.", nameof(prefab));
        return this;
    }

    public IInstanceFactory Build()
    {
        _frozen = true;
        return this;
    }

    public T Create<T>(PrefabRef<T> prefab, in PrefabSpawnContext context)
        where T : GameInstance
    {
        if (prefab.IsEmpty)
            throw new ArgumentException("Prefab reference cannot be empty.", nameof(prefab));
        if (!_registrations.TryGetValue(prefab.Name, out IRegistration? registration))
            throw new KeyNotFoundException($"Prefab '{prefab.Name}' is not registered.");
        if (registration is not Registration<T> typed)
        {
            throw new InvalidOperationException(
                $"Prefab '{prefab.Name}' creates '{registration.InstanceType.Name}', not '{typeof(T).Name}'.");
        }

        T instance = typed.Create(context);
        return instance ?? throw new InvalidOperationException(
            $"Prefab '{prefab.Name}' returned null.");
    }

    private interface IRegistration
    {
        Type InstanceType { get; }
    }

    private sealed class Registration<T>(Func<PrefabSpawnContext, T> create) : IRegistration
        where T : GameInstance
    {
        public Type InstanceType => typeof(T);

        public T Create(in PrefabSpawnContext context) => create(context);
    }
}
