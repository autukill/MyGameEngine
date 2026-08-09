namespace GameEngine.Features.Bloom.Infrastructure;

using System.Numerics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;
using Silk.NET.OpenGL;

internal sealed class BloomPass : RenderPass
{
    private readonly GL _gl;
    private readonly RenderTarget2D _source;
    private readonly RenderTarget2D _bright;
    private readonly RenderTarget2D _ping;
    private readonly RenderTarget2D _pong;
    private readonly BloomExtractShader _extractShader;
    private readonly GaussianBlurShader _blurShader;
    private readonly IReadOnlyList<RenderTarget2D> _inputs;
    private readonly uint _vao;
    private readonly uint _vbo;
    private BloomSettings _settings;

    public override RenderTarget2D? Output => _pong;
    public override IEnumerable<RenderTarget2D> Inputs => _inputs;

    public BloomPass(
        string name,
        GL gl,
        RenderTarget2D source,
        RenderTarget2D bright,
        RenderTarget2D ping,
        RenderTarget2D pong,
        BloomExtractShader extractShader,
        GaussianBlurShader blurShader,
        BloomSettings settings) : base(name)
    {
        _gl = gl;
        _source = source;
        _bright = bright;
        _ping = ping;
        _pong = pong;
        _extractShader = extractShader;
        _blurShader = blurShader;
        _settings = settings;
        _inputs = new[] { source };
        (_vao, _vbo) = CreateFullscreenQuad(gl);
    }

    public void UpdateSettings(BloomSettings settings) => _settings = settings;

    public override void Execute(in RenderPassContext context)
    {
        RenderExtract(context.Statistics);
        RenderTarget2D current = _bright;
        for (int iteration = 0; iteration < _settings.Iterations; iteration++)
        {
            RenderBlur(current, _ping, horizontal: true, multiplier: 1f, context.Statistics);
            float multiplier = iteration == _settings.Iterations - 1
                ? _settings.Intensity
                : 1f;
            RenderBlur(_ping, _pong, horizontal: false, multiplier, context.Statistics);
            current = _pong;
        }
    }

    private void RenderExtract(
        GameEngine.Core.Infrastructure.Diagnostics.IFrameStatisticsSink? statistics)
    {
        PrepareTarget(_bright);
        BindTexture(_source.ColorTexture);
        _extractShader.SetThreshold(_settings.Threshold);
        Draw(statistics);
    }

    private void RenderBlur(
        RenderTarget2D source,
        RenderTarget2D target,
        bool horizontal,
        float multiplier,
        GameEngine.Core.Infrastructure.Diagnostics.IFrameStatisticsSink? statistics)
    {
        PrepareTarget(target);
        BindTexture(source.ColorTexture);
        Vector2 direction = horizontal
            ? new Vector2(_settings.BlurRadius / target.Width, 0f)
            : new Vector2(0f, _settings.BlurRadius / target.Height);
        _blurShader.SetDirectionAndMultiplier(direction, multiplier);
        Draw(statistics);
    }

    private void PrepareTarget(RenderTarget2D target)
    {
        target.SetAsTarget();
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        BlendState.Opaque.Apply(_gl);
        DepthStencilState.None.Apply(_gl);
    }

    private void BindTexture(uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    private void Draw(
        GameEngine.Core.Infrastructure.Diagnostics.IFrameStatisticsSink? statistics)
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        statistics?.RecordDrawCall();
        _gl.BindVertexArray(0);
    }

    internal static (int Width, int Height) CalculateTargetSize(
        int width, int height, BloomResolution resolution)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (!Enum.IsDefined(resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
        int divisor = (int)resolution;
        return (Math.Max(1, (width - 1) / divisor + 1),
                Math.Max(1, (height - 1) / divisor + 1));
    }

    internal static int CalculateInternalDrawCount(int iterations) => 1 + iterations * 2;

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
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
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
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
    }
}
