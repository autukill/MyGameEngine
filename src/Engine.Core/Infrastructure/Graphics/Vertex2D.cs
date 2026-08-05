namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// 2D 渲染顶点（对齐 32 字节内存）
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Vertex2D
{
    public Vector2 Position;  // 8 bytes
    public Vector2 TexCoord;  // 8 bytes
    public Vector4 Color;     // 16 bytes

    public Vertex2D(Vector2 position, Vector2 texCoord, Vector4 color)
    {
        Position = position;
        TexCoord = texCoord;
        Color = color;
    }
}
