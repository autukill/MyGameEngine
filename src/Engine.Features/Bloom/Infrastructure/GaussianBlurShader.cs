namespace GameEngine.Features.Bloom.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using Silk.NET.OpenGL;

public sealed class GaussianBlurShader : IShader
{
    private readonly GL _gl;
    private readonly int _directionLocation;
    private readonly int _multiplierLocation;
    public uint Handle { get; }

    public GaussianBlurShader(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        Handle = ShaderProgram.Create(
            gl, BloomExtractShader.VertexSource, FragmentSource, nameof(GaussianBlurShader));
        Use();
        int texture = gl.GetUniformLocation(Handle, "uTexture");
        if (texture >= 0) gl.Uniform1(texture, 0);
        _directionLocation = gl.GetUniformLocation(Handle, "uDirection");
        _multiplierLocation = gl.GetUniformLocation(Handle, "uMultiplier");
    }

    public void Use() => _gl.UseProgram(Handle);
    public void SetProjection(Matrix4x4 matrix) { }

    public void SetDirectionAndMultiplier(Vector2 direction, float multiplier)
    {
        Use();
        if (_directionLocation >= 0)
            _gl.Uniform2(_directionLocation, direction.X, direction.Y);
        if (_multiplierLocation >= 0)
            _gl.Uniform1(_multiplierLocation, multiplier);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    private const string FragmentSource = """
        #version 330 core
        in vec2 vUv;
        out vec4 FragColor;
        uniform sampler2D uTexture;
        uniform vec2 uDirection;
        uniform float uMultiplier;
        void main() {
            vec3 color = texture(uTexture, vUv).rgb * 0.227027;
            color += texture(uTexture, vUv + uDirection * 1.384615).rgb * 0.316216;
            color += texture(uTexture, vUv - uDirection * 1.384615).rgb * 0.316216;
            color += texture(uTexture, vUv + uDirection * 3.230769).rgb * 0.070270;
            color += texture(uTexture, vUv - uDirection * 3.230769).rgb * 0.070270;
            FragColor = vec4(color * uMultiplier, 1.0);
        }
        """;
}
