namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 实例唯一标识（强类型 ID，防止与其他 Guid 混淆）。
/// 使用 Guid.CreateVersion7()（.NET 10）生成时序有序 ID，利于索引。
/// </summary>
public readonly record struct InstanceId(Guid Value) : IComparable<InstanceId>
{
    /// <summary>生成新的时序有序 InstanceId</summary>
    public static InstanceId New() => new(Guid.CreateVersion7());

    public static InstanceId Empty => new(Guid.Empty);

    public int CompareTo(InstanceId other) => Value.CompareTo(other.Value);

    public override string ToString() => $"Instance[{Value.ToString("D")[..8]}]";
}
