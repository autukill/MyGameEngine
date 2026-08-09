namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Diagnostics;

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

    // ---- 实例级渲染状态机（变更即 Flush + Apply，与纹理打断同构） ----
    private BlendMode _currentBlendMode = BlendMode.AlphaBlend;
    private bool _currentDepthTest;
    private bool _currentDepthWrite;
    private uint _currentShaderHandle = 0;
    private MaterialRef? _currentMaterial;
    private long _currentMaterialRevision;

    /// <summary>默认 shader（SetShader(null) 时切回；由组合根/Pass 注入）</summary>
    public IShader? DefaultShader { get; set; }

    /// <summary>ShaderRef → program handle 解析器（由组合根注入 ShaderLibrary）</summary>
    public IShaderResolver? ShaderResolver { get; set; }

    /// <summary>SpriteRef → 元数据/GPU 帧解析器（由组合根注入 SpriteLibrary）</summary>
    public ISpriteResolver? SpriteResolver { get; set; }

    /// <summary>可选帧统计入口；为 null 时不采集。</summary>
    public IFrameStatisticsSink? Statistics { get; set; }

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
        {
            Statistics?.RecordTextureSwitch();
            Flush();
        }

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

    public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata)
    {
        if (SpriteResolver is not null)
            return SpriteResolver.TryGetMetadata(sprite, out metadata);
        metadata = default;
        return false;
    }

    public void DrawSpriteCommand(in SpriteDrawCommand command)
    {
        if (!_isBegin) throw new InvalidOperationException("Call SpriteBatch.Begin() first.");
        if (command.Sprite.IsEmpty || SpriteResolver is null) return;

        int subImage = (int)MathF.Floor(command.SubImage);
        if (!SpriteResolver.TryResolve(command.Sprite, subImage, out var frame)) return;

        if (_currentTextureHandle != 0 && _currentTextureHandle != frame.TextureHandle)
        {
            Statistics?.RecordTextureSwitch();
            Flush();
        }
        if (_quadCount >= MaxQuads)
            Flush();

        _currentTextureHandle = frame.TextureHandle;
        int vIndex = _quadCount * 4;
        Span<Vector2> corners = stackalloc Vector2[4];
        SpriteGeometry.CalculateCorners(command, frame, corners);
        Vector4 uv = frame.UvBounds;

        _vertexBuffer[vIndex + 0] = new Vertex2D(
            corners[0], new Vector2(uv.X, uv.Y), command.Color);
        _vertexBuffer[vIndex + 1] = new Vertex2D(
            corners[1], new Vector2(uv.Z, uv.Y), command.Color);
        _vertexBuffer[vIndex + 2] = new Vertex2D(
            corners[2], new Vector2(uv.Z, uv.W), command.Color);
        _vertexBuffer[vIndex + 3] = new Vertex2D(
            corners[3], new Vector2(uv.X, uv.W), command.Color);

        _quadCount++;
    }

    /// <summary>
    /// 切换混合模式（GMS gpu_set_blendmode）。状态未变化时零开销；变化时 Flush + Apply。
    /// </summary>
    public void SetBlendMode(BlendMode mode)
    {
        if (mode == _currentBlendMode) return;
        Flush();
        _currentBlendMode = mode;
        ApplyBlendMode(mode);
    }

    /// <summary>
    /// 切换深度测试/写入状态。状态未变化时零开销；变化时 Flush + Apply。
    /// </summary>
    public void SetDepthState(bool depthTest, bool depthWrite)
    {
        if (depthTest == _currentDepthTest && depthWrite == _currentDepthWrite) return;
        Flush();
        _currentDepthTest = depthTest;
        _currentDepthWrite = depthWrite;
        ApplyDepthState(depthTest, depthWrite);
    }

    /// <summary>
    /// 切换 Shader（GMS shader_set）。null = 默认 shader。
    /// 状态未变化时零开销；变化时 Flush + UseProgram。
    /// </summary>
    public void SetShader(ShaderRef? shader)
    {
        uint handle = 0;
        if (shader is { IsEmpty: false } s && ShaderResolver is not null)
            handle = ShaderResolver.Resolve(s);

        if (handle == _currentShaderHandle && _currentMaterial is null) return;
        Flush();
        _currentShaderHandle = handle;
        _currentMaterial = null;
        _currentMaterialRevision = 0;
        if (handle != 0) _gl.UseProgram(handle);
        else DefaultShader?.Use();
    }

    public void SetMaterial(MaterialRef? material)
    {
        if (material is not { IsEmpty: false } reference || ShaderResolver is null ||
            !ShaderResolver.TryResolveMaterial(reference, out ResolvedMaterial resolved))
        {
            SetShader(null);
            return;
        }

        if (_currentMaterial == reference &&
            _currentShaderHandle == resolved.ProgramHandle &&
            _currentMaterialRevision == resolved.ParameterRevision)
            return;

        Flush();
        _currentShaderHandle = resolved.ProgramHandle;
        _currentMaterial = reference;
        _currentMaterialRevision = resolved.ParameterRevision;
        ShaderResolver.ApplyMaterial(reference);
    }

    private void ApplyBlendMode(BlendMode mode)
    {
        switch (mode)
        {
            case BlendMode.Opaque:
                _gl.Disable(EnableCap.Blend);
                break;
            case BlendMode.Additive:
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
            default: // AlphaBlend
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    private void ApplyDepthState(bool depthTest, bool depthWrite)
    {
        if (depthTest) _gl.Enable(EnableCap.DepthTest);
        else _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(depthWrite);
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

        Statistics?.RecordDrawCall();
        Statistics?.RecordBatchFlush();

        _quadCount = 0;
    }

    public void End()
    {
        if (!_isBegin) throw new InvalidOperationException("SpriteBatch.End() without Begin().");
        Flush();
        _isBegin = false;
        _currentTextureHandle = 0;

        // 统一复位默认状态：防止实例间/Pass 间状态泄漏（推演：复位点在 End() 而非每实例）
        if (_currentBlendMode != BlendMode.AlphaBlend)
        {
            _currentBlendMode = BlendMode.AlphaBlend;
            ApplyBlendMode(BlendMode.AlphaBlend);
        }
        if (_currentDepthTest || _currentDepthWrite)
        {
            _currentDepthTest = false;
            _currentDepthWrite = false;
            ApplyDepthState(false, false);
        }
        if (_currentShaderHandle != 0)
        {
            _currentShaderHandle = 0;
            _currentMaterial = null;
            _currentMaterialRevision = 0;
            DefaultShader?.Use();
        }
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}
