namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 图层深度值对象（对应 GMS 的 depth 概念）。
/// 注意：GMS 中 depth 数值小的先绘制（在底层），数值大的后绘制（在顶层）。
/// 此约定与本引擎的 Layer.DepthOrder 字段语义一致。
/// </summary>
public readonly record struct LayerDepth(int Value) : IComparable<LayerDepth>
{
    public static LayerDepth Background => new(10000);
    public static LayerDepth Instances => new(0);
    public static LayerDepth UI => new(-10000);

    public int CompareTo(LayerDepth other) => Value.CompareTo(other.Value);

    public static bool operator <(LayerDepth a, LayerDepth b) => a.Value < b.Value;
    public static bool operator >(LayerDepth a, LayerDepth b) => a.Value > b.Value;
    public static bool operator <=(LayerDepth a, LayerDepth b) => a.Value <= b.Value;
    public static bool operator >=(LayerDepth a, LayerDepth b) => a.Value >= b.Value;
}
