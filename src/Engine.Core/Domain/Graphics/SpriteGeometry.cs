namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;

/// <summary>Sprite Quad 的纯几何计算；供 SpriteBatch 与无 GL 测试复用。</summary>
public static class SpriteGeometry
{
    public static void CalculateCorners(
        in SpriteDrawCommand command,
        in ResolvedSpriteFrame frame,
        Span<Vector2> destination)
    {
        if (destination.Length < 4)
            throw new ArgumentException("Destination must contain at least four elements.", nameof(destination));

        Vector2 size = command.SizeOverride ?? frame.Size;
        Vector2 origin = command.OriginOverride ?? frame.Origin;

        destination[0] = new Vector2(-origin.X, -origin.Y);
        destination[1] = new Vector2(size.X - origin.X, -origin.Y);
        destination[2] = new Vector2(size.X - origin.X, size.Y - origin.Y);
        destination[3] = new Vector2(-origin.X, size.Y - origin.Y);

        float c = MathF.Cos(command.RotationRadians);
        float s = MathF.Sin(command.RotationRadians);
        for (int i = 0; i < 4; i++)
        {
            Vector2 local = destination[i] * command.Scale;
            // 世界坐标 Y 向下：该公式保证正角度在屏幕上表现为逆时针。
            destination[i] = new Vector2(
                local.X * c + local.Y * s,
                -local.X * s + local.Y * c) + command.Position;
        }
    }
}
