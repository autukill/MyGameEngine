namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// A weak, strongly typed reference to a Scene instance. It stores only identity and never extends
/// the target lifetime; resolution returns null after the target leaves the current Scene.
/// </summary>
public readonly record struct InstanceRef<T>(InstanceId Id)
    where T : GameInstance
{
    public static InstanceRef<T> Empty => new(InstanceId.Empty);
    public bool IsEmpty => Id == InstanceId.Empty;

    public override string ToString() => IsEmpty
        ? $"InstanceRef<{typeof(T).Name}>[Empty]"
        : $"InstanceRef<{typeof(T).Name}>[{Id.Value.ToString("D")[..8]}]";
}

public static class InstanceRefExtensions
{
    /// <summary>Captures the instance identity without retaining the instance object.</summary>
    public static InstanceRef<T> ToInstanceRef<T>(this T instance)
        where T : GameInstance
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new InstanceRef<T>(instance.Id);
    }
}
