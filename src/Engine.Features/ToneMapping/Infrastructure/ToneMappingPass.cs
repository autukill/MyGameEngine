namespace GameEngine.Features.ToneMapping.Infrastructure;

using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using GameEngine.Features.ToneMapping.Domain;
using Silk.NET.OpenGL;

internal sealed class ToneMappingPass : RenderPass
{
    private readonly GL _gl;
    private readonly RenderTarget2D _scene;
    private readonly RenderTarget2D? _bloom;
    private readonly RenderTarget2D _output;
    private readonly ToneMappingShader _shader;
    private readonly IReadOnlyList<RenderTarget2D> _inputs;
    private readonly uint _vao;
    private readonly uint _vbo;
    private ToneMappingSettings _settings;

    public override RenderTarget2D Output => _output;
    public override IEnumerable<RenderTarget2D> Inputs => _inputs;

    public ToneMappingPass(
        string name,
        GL gl,
        RenderTarget2D scene,
        RenderTarget2D? bloom,
        RenderTarget2D output,
        ToneMappingShader shader,
        ToneMappingSettings settings) : base(name)
    {
        _gl = gl;
        _scene = scene;
        _bloom = bloom;
        _output = output;
        _shader = shader;
        _settings = settings;
        _inputs = bloom is null ? new[] { scene } : new[] { scene, bloom };
        (_vao, _vbo) = CreateFullscreenQuad(gl);
    }

    public void UpdateSettings(ToneMappingSettings settings) => _settings = settings;

    public override void Execute(in RenderPassContext context)
    {
        _output.SetAsTarget();
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        BlendState.Opaque.Apply(_gl);
        DepthStencilState.None.Apply(_gl);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _scene.ColorTexture);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _bloom?.ColorTexture ?? 0);
        _shader.SetSettings(_settings, _bloom is not null);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    public override void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }

    private static unsafe (uint Vao, uint Vbo) CreateFullscreenQuad(GL gl)
    {
        float[] vertices =
        [
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        ];
        uint vao = gl.GenVertexArray();
        uint vbo = gl.GenBuffer();
        gl.BindVertexArray(vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        fixed (float* data = vertices)
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false,
            4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.BindVertexArray(0);
        return (vao, vbo);
    }
}
