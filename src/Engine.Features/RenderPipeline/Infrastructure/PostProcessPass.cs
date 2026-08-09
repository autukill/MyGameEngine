namespace GameEngine.Features.RenderPipeline.Infrastructure;

using Silk.NET.OpenGL;
using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// 全屏后处理 Pass：把输入纹理通过自定义 Shader 渲染到输出。
/// 用于 Bloom / Tonemap / ColorGrading / CRT / VHS 等。
/// </summary>
public sealed class PostProcessPass : RenderPass
{
    private readonly GL _gl;
    private readonly IShader _postShader;
    private readonly RenderTarget2D _input;
    private readonly RenderTarget2D? _output;
    private readonly uint _fullscreenVao;
    private readonly uint _fullscreenVbo;

    public override RenderTarget2D? Output => _output;
    public override IEnumerable<RenderTarget2D> Inputs => new[] { _input };

    public PostProcessPass(string name, GL gl, IShader postShader,
        RenderTarget2D input, RenderTarget2D? output = null) : base(name)
    {
        _gl = gl;
        _postShader = postShader;
        _input = input;
        _output = output;
        (_fullscreenVao, _fullscreenVbo) = CreateFullscreenQuad(gl);
    }

    public override void Execute(in RenderPassContext ctx)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _input.ColorTexture);

        _postShader.Use();
        _postShader.SetProjection(Matrix4x4.CreateOrthographicOffCenter(-1, 1, -1, 1, -1, 1));

        BlendState.Opaque.Apply(_gl);
        DepthStencilState.None.Apply(_gl);

        _gl.BindVertexArray(_fullscreenVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        ctx.RecordDrawCall();
        _gl.BindVertexArray(0);
    }

    private static unsafe (uint Vao, uint Vbo) CreateFullscreenQuad(GL gl)
    {
        // 顶点：position(xy) + uv(uv) —— 两个三角形拼成全屏矩形
        float[] vertices = {
            -1f, -1f,    0f, 0f,
             1f, -1f,    1f, 0f,
             1f,  1f,    1f, 1f,
            -1f, -1f,    0f, 0f,
             1f,  1f,    1f, 1f,
            -1f,  1f,    0f, 1f,
        };

        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* p = vertices)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.BindVertexArray(0);
        return (vao, vbo);
    }

    public override void Dispose()
    {
        _gl.DeleteVertexArray(_fullscreenVao);
        _gl.DeleteBuffer(_fullscreenVbo);
    }
}
