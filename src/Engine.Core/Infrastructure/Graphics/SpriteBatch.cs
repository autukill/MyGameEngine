namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 2D 精灵批处理器（内存池 + 动态 VBO/VAO，减少 GL 调用）
/// </summary>
public unsafe class SpriteBatch : IDisposable {
    private const int MaxQuads = 2048; // 单次 Batch 最大 Quad 数量
    private const int MaxVertices = MaxQuads * 4; // 8192 Vertices
    private const int MaxIndices = MaxQuads * 6; // 12288 Indices

    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;

    private readonly Vertex2D[] _vertexBuffer = new Vertex2D[MaxVertices];
    private int _quadCount = 0;

    private uint _currentTextureHandle = 0;
    private bool _isBeginning = false;

    public SpriteBatch( GL gl ) {
        _gl = gl;

        // 1. 创建并绑定 VAO
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray( _vao );

        // 2. 创建动态 VBO (分配内存空间，不传初始数据)
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer( BufferTargetARB.ArrayBuffer, _vbo );
        _gl.BufferData( BufferTargetARB.ArrayBuffer, (nuint)(MaxVertices * sizeof(Vertex2D)), null, BufferUsageARB.DynamicDraw );

        // 3. 创建静态 EBO 并填充全量 Quad 索引数据
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer( BufferTargetARB.ElementArrayBuffer, _ebo );

        ushort[] indices = new ushort[MaxIndices];
        ushort offset = 0;
        for (int i = 0; i < MaxIndices; i += 6) {
            indices[i + 0] = (ushort)(offset + 0);
            indices[i + 1] = (ushort)(offset + 1);
            indices[i + 2] = (ushort)(offset + 2);
            indices[i + 3] = (ushort)(offset + 2);
            indices[i + 4] = (ushort)(offset + 3);
            indices[i + 5] = (ushort)(offset + 0);
            offset += 4;
        }

        fixed (ushort* iPtr = indices) {
            _gl.BufferData( BufferTargetARB.ElementArrayBuffer, (nuint)(MaxIndices * sizeof(ushort)), iPtr, BufferUsageARB.StaticDraw );
        }

        // 4. 设置 Vertex Attribute Pointers
        // Location 0: Position (Vector2)
        _gl.EnableVertexAttribArray( 0 );
        _gl.VertexAttribPointer( 0, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)0 );

        // Location 1: TexCoord (Vector2)
        _gl.EnableVertexAttribArray( 1 );
        _gl.VertexAttribPointer( 1, 2, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)sizeof(Vector2) );

        // Location 2: Color (Vector4)
        _gl.EnableVertexAttribArray( 2 );
        _gl.VertexAttribPointer( 2, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vertex2D), (void*)(sizeof(Vector2) * 2) );

        _gl.BindVertexArray( 0 );
    }

    /// <summary>
    /// 开始 Batch 绘制
    /// </summary>
    public void Begin() {
        if ( _isBeginning ) throw new InvalidOperationException( "SpriteBatch.Begin() has already been called." );

        _isBeginning = true;
        _quadCount = 0;
        _currentTextureHandle = 0;
    }

    /// <summary>
    /// 提交精灵（类似 GameMaker draw_sprite）
    /// </summary>
    public void Draw( uint textureHandle, Vector2 position, Vector2 size, Vector4 color, Vector4 uvBounds = default ) {
        if ( !_isBeginning ) throw new InvalidOperationException( "Call SpriteBatch.Begin() first." );

        // 默认全图 UV (U0=0, V0=0, U1=1, V1=1)
        if ( uvBounds == default ) uvBounds = new Vector4( 0, 0, 1, 1 );

        // 检测 1：纹理改变，立刻 Flush 之前批次
        if ( _currentTextureHandle != 0 && _currentTextureHandle != textureHandle ) {
            Flush();
        }

        // 检测 2：Buffer 溢出，立刻 Flush
        if ( _quadCount >= MaxQuads ) {
            Flush();
        }

        _currentTextureHandle = textureHandle;

        // 计算 4 个角的位置
        int vIndex = _quadCount * 4;

        float x1 = position.X;
        float y1 = position.Y;
        float x2 = position.X + size.X;
        float y2 = position.Y + size.Y;

        // 填充 4 个顶点数据 (顺时针)
        _vertexBuffer[vIndex + 0] = new Vertex2D( new Vector2( x1, y1 ), new Vector2( uvBounds.X, uvBounds.Y ), color ); // Top-Left
        _vertexBuffer[vIndex + 1] = new Vertex2D( new Vector2( x2, y1 ), new Vector2( uvBounds.Z, uvBounds.Y ), color ); // Top-Right
        _vertexBuffer[vIndex + 2] = new Vertex2D( new Vector2( x2, y2 ), new Vector2( uvBounds.Z, uvBounds.W ), color ); // Bottom-Right
        _vertexBuffer[vIndex + 3] = new Vertex2D( new Vector2( x1, y2 ), new Vector2( uvBounds.X, uvBounds.W ), color ); // Bottom-Left

        _quadCount++;
    }

    /// <summary>
    /// 将缓冲区的顶点数据批量上传 GPU 并执行一次 DrawCall
    /// </summary>
    public void Flush() {
        if ( _quadCount == 0 || _currentTextureHandle == 0 ) return;

        // 1. 绑定当前 Batch 的纹理到 Texture Unit 0
        _gl.ActiveTexture( TextureUnit.Texture0 );
        _gl.BindTexture( TextureTarget.Texture2D, _currentTextureHandle );

        // 2. 将 CPU 端的 Vertex2D 数组通过 Span 零拷贝更新到 GPU VBO
        _gl.BindBuffer( BufferTargetARB.ArrayBuffer, _vbo );

        ReadOnlySpan<Vertex2D> vertexSpan = _vertexBuffer.AsSpan( 0, _quadCount * 4 );
        fixed (Vertex2D* vPtr = vertexSpan) {
            _gl.BufferSubData( BufferTargetARB.ArrayBuffer, 0, (nuint)(_quadCount * 4 * sizeof(Vertex2D)), vPtr );
        }

        // 3. 执行单次索引绘制 (DrawElements)
        _gl.BindVertexArray( _vao );
        _gl.DrawElements( PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedShort, null );
        _gl.BindVertexArray( 0 );

        // 4. 重置计数器，准备填充下一个 Batch
        _quadCount = 0;
    }

    /// <summary>
    /// 结束当前 Batch 绘制
    /// </summary>
    public void End() {
        if ( !_isBeginning ) return;

        Flush(); // 提交剩余未绘制的 Quads
        _isBeginning = false;
    }

    public void Dispose() {
        _gl.DeleteVertexArray( _vao );
        _gl.DeleteBuffer( _vbo );
        _gl.DeleteBuffer( _ebo );
    }
}