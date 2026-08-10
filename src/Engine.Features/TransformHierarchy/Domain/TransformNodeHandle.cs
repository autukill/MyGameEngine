namespace GameEngine.Features.TransformHierarchy.Domain;

/// <summary>
/// Stable reference to a hierarchy slot. A slot generation changes whenever it is reused,
/// and the owning hierarchy identity prevents accidental cross-hierarchy access.
/// </summary>
public readonly record struct TransformNodeHandle
{
    internal TransformNodeHandle(int index, ulong generation, long hierarchyId)
    {
        Index = index;
        Generation = generation;
        HierarchyId = hierarchyId;
    }

    public int Index { get; }
    public ulong Generation { get; }
    internal long HierarchyId { get; }

    public bool IsEmpty => HierarchyId == 0 || Generation == 0;
    public static TransformNodeHandle None => default;

    public override string ToString() =>
        IsEmpty ? "TransformNodeHandle.None" : $"TransformNodeHandle({Index}:{Generation})";
}
