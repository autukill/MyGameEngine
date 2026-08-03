namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 2D 向量位置（值对象：不可变，零 GC 分配）
/// </summary>
public readonly record struct Vector2D(float X, float Y)
{
    public static Vector2D Zero => new(0f, 0f);
    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
}

/// <summary>
/// 2D 变换组件（包含位置、旋转、缩放）
/// </summary>
public readonly record struct Transform2D(Vector2D Position, float Rotation, Vector2D Scale)
{
    public static Transform2D Default => new(Vector2D.Zero, 0f, new Vector2D(1f, 1f));
}

/// <summary>
/// 实例唯一标识（强类型 ID）
/// </summary>
public readonly record struct InstanceId(Guid Value)
{
    public static InstanceId New() => new(Guid.NewGuid());
}

/// <summary>
/// 图层深度（对应 GMS 的 Depth，深度小的优先绘制）
/// </summary>
public readonly record struct LayerDepth(int Value) : IComparable<LayerDepth>
{
    public int CompareTo(LayerDepth other) => Value.CompareTo(other.Value);
}
