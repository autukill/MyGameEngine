namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 2D 变换值对象（位置 + 旋转 + 缩放）。
/// 不可变、零 GC 分配，使用 .NET 10 readonly record struct。
/// </summary>
public readonly record struct Transform2D(Vector2D Position, float Rotation, Vector2D Scale)
{
    public static Transform2D Default => new(Vector2D.Zero, 0f, new Vector2D(1f, 1f));

    /// <summary>平移变换</summary>
    public Transform2D Translate(Vector2D delta) =>
        this with { Position = Position + delta };

    /// <summary>绕原点旋转变换（弧度）</summary>
    public Transform2D Rotate(float deltaRadians) =>
        this with { Rotation = Rotation + deltaRadians };

    /// <summary>缩放变换</summary>
    public Transform2D ScaleBy(Vector2D factor) =>
        this with { Scale = new Vector2D(Scale.X * factor.X, Scale.Y * factor.Y) };
}
