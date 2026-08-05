namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using GameEngine.Core.Domain.Graphics;

/// <summary>
/// 高性能 2D Sprite Batch：零 GC、动态 VBO、静态 EBO、自动状态打断。
/// 单次最多 2048 Quad (8192 顶点)。
/// 实现 ISpriteBatch 接口，让 Domain 层的 GameInstance.OnDraw 不直接依赖本类。
/// </summary>
public unsafe class SpriteBatch : ISpriteBatch, IDisposable
{
    private const int MaxQuads = 2048;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;

    private readonly Vertex2D[] _vertexBuffer = new Vertex2D[MaxVertices];
    private int _quadCount = 0;

    private uint _currentTextureHandle = 0;
    private bool _isBegin = false;

    public SpriteBatch(GL gl)
    {
        _gl = gl;

        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer,
            (nuint)(MaxVertices * sizeof(Vertex2D)), null, BufferUsageARB.DynamicDraw);

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        // 预生成全量 Quad 索引
        ushort[] indices = new ushort[MaxIndices];
        ushort offset = 0;
        for (int i = 0; i < MaxIndices; i += 6)
        {
            indices[i + 0] = (ushort)(offset + 0);
            indices[i + 1] = (ushort)(offset + 1);
            indices[i + 2] = (ushort)(offset + 2);
            indices[i + 3] = (ushort)(offset + 2);
            indices[i + 4] = (ushort)(offset + 3);
            indices[i + 5] = (ushort)(offset + 0);
            offset += 4;
        }
        fixed (ushort* iPtr = indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                (nuint)(MaxIndices * sizeof(ushort)), iPtr, BufferUsageARB.StaticDraw);
        }

        // Vertex Attribute Pointers
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
            (uint)sizeof(Vertex2D), (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            (uint)sizeof(Vertex2D), (void*)sizeof(Vector2));

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false,
            (uint)sizeof(Vertex2D), (void*)(sizeof(Vector2) * 2));

        _gl.BindVertexArray(0);
    }

    public void Begin()
    {
        if (_isBegin) throw new InvalidOperationException("SpriteBatch.Begin() already called.");
        _isBegin = true;
        _quadCount = 0;
        _currentTextureHandle = 0;
    }

    public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color,
        Vector4 uvBounds = default)
    {
        if (!_isBegin) throw new InvalidOperationException("Call SpriteBatch.Begin() first.");

        if (uvBounds == default) uvBounds = new Vector4(0, 0, 1, 1);

        // 纹理切换 → 立即 Flush
        if (_currentTextureHandle != 0 && _currentTextureHandle != textureHandle)
            Flush();

        // 缓冲溢出 → 立即 Flush
        if (_quadCount >= MaxQuads)
            Flush();

        _currentTextureHandle = textureHandle;

        int vIndex = _quadCount * 4;

        float x1 = position.X;
        float y1 = position.Y;
        float x2 = position.X + size.X;
        float y2 = position.Y + size.Y;

        _vertexBuffer[vIndex + 0] = new Vertex2D(
            new Vector2(x1, y1), new Vector2(uvBounds.X, uvBounds.Y), color);
        _vertexBuffer[vIndex + 1] = new Vertex2D(
            new Vector2(x2, y1), new Vector2(uvBounds.Z, uvBounds.Y), color);
        _vertexBuffer[vIndex + 2] = new Vertex2D(
            new Vector2(x2, y2), new Vector2(uvBounds.Z, uvBounds.W), color);
        _vertexBuffer[vIndex + 3] = new Vertex2D(
            new Vector2(x1, y2), new Vector2(uvBounds.X, uvBounds.W), color);

        _quadCount++;
    }

    public void Flush()
    {
        if (_quadCount == 0 || _currentTextureHandle == 0) return;

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _currentTextureHandle);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        ReadOnlySpan<Vertex2D> vertexSpan = _vertexBuffer.AsSpan(0, _quadCount * 4);
        fixed (Vertex2D* vPtr = vertexSpan)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer,
                0, (nuint)(_quadCount * 4 * sizeof(Vertex2D)), vPtr);
        }

        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles,
            (uint)(_quadCount * 6), DrawElementsType.UnsignedShort, null);

        _quadCount = 0;
    }

    public void End()
    {
        if (!_isBegin) throw new InvalidOperationException("SpriteBatch.End() without Begin().");
        Flush();
        _isBegin = false;
        _currentTextureHandle = 0;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}
