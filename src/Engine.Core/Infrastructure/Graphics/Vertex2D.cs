namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// 2D 渲染顶点（顺序布局，32 字节内存对齐）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Vertex2D
{
    public Vector2 Position; // 8 bytes (X, Y)
    public Vector2 TexCoord; // 8 bytes (U, V)
    public Vector4 Color;    // 16 bytes (R, G, B, A)

    public Vertex2D(Vector2 position, Vector2 texCoord, Vector4 color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }
}
