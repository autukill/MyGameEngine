namespace GameEngine.Features.Bloom.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using Silk.NET.OpenGL;

public sealed class BloomExtractShader : IShader
{
    private readonly GL _gl;
    private readonly int _thresholdLocation;
    public uint Handle { get; }

    public BloomExtractShader(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        Handle = ShaderProgram.Create(gl, VertexSource, FragmentSource, nameof(BloomExtractShader));
        Use();
        int texture = gl.GetUniformLocation(Handle, "uTexture");
        if (texture >= 0) gl.Uniform1(texture, 0);
        _thresholdLocation = gl.GetUniformLocation(Handle, "uThreshold");
    }

    public void Use() => _gl.UseProgram(Handle);
    public void SetProjection(Matrix4x4 matrix) { }

    public void SetThreshold(float threshold)
    {
        Use();
        if (_thresholdLocation >= 0) _gl.Uniform1(_thresholdLocation, threshold);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    internal const string VertexSource = """
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;
        out vec2 vUv;
        void main() {
            gl_Position = vec4(aPos, 0.0, 1.0);
            vUv = aTexCoord;
        }
        """;

    private const string FragmentSource = """
        #version 330 core
        in vec2 vUv;
        out vec4 FragColor;
        uniform sampler2D uTexture;
        uniform float uThreshold;
        void main() {
            vec3 color = texture(uTexture, vUv).rgb;
            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
            FragColor = vec4(luminance >= uThreshold ? color : vec3(0.0), 1.0);
        }
        """;
}
