namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

public class SpriteShader : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    private const string VertexShaderSource = @"
        #version 330 core
        layout (location = 0) in vec2 aPos;
        layout (location = 1) in vec2 aTexCoord;
        layout (location = 2) in vec4 aColor;

        out vec2 Frag_TexCoord;
        out vec4 Frag_Color;

        uniform mat4 uProjection;

        void main() {
            gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
            Frag_TexCoord = aTexCoord;
            Frag_Color = aColor;
        }";

    private const string FragmentShaderSource = @"
        #version 330 core
        in vec2 Frag_TexCoord;
        in vec4 Frag_Color;

        out vec4 FragColor;

        uniform sampler2D uTexture;

        void main() {
            FragColor = texture(uTexture, Frag_TexCoord) * Frag_Color;
        }";

    public SpriteShader(GL gl)
    {
        _gl = gl;
        
        uint vert = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint frag = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vert);
        _gl.AttachShader(Handle, frag);
        _gl.LinkProgram(Handle);

        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uProjection");
        unsafe
        {
            _gl.UniformMatrix4(loc, 1, false, (float*)&matrix);
        }
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}