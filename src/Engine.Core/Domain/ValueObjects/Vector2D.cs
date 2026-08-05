namespace GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 2D 向量值对象（不可变，零 GC 分配）。
/// 对应 GMS 中的 x/y 坐标对，但封装为强类型避免参数顺序错误。
/// </summary>
public readonly record struct Vector2D(float X, float Y)
{
    public static Vector2D Zero => new(0f, 0f);
    public static Vector2D One => new(1f, 1f);
    public static Vector2D UnitX => new(1f, 0f);
    public static Vector2D UnitY => new(0f, 1f);

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator *(Vector2D a, float s) => new(a.X * s, a.Y * s);
    public static Vector2D operator *(float s, Vector2D a) => new(a.X * s, a.Y * s);

    public float Length() => MathF.Sqrt(X * X + Y * Y);
    public float LengthSquared() => X * X + Y * Y;
    public Vector2D Normalize() => Length() is float l and > 0 ? this * (1f / l) : Zero;

    public float Dot(Vector2D other) => X * other.X + Y * other.Y;

    public override string ToString() => $"({X:F2}, {Y:F2})";
}

