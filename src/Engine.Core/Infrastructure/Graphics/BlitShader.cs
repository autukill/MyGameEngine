namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 简单 Blit Shader：把输入纹理直接绘制到全屏 Quad（用于合成 Pass）。
/// </summary>
public sealed class BlitShader : IShader
{
    private readonly GL _gl;
    public uint Handle { get; }

    public BlitShader(GL gl)
    {
        _gl = gl;
        uint vert = CompileShader(ShaderType.VertexShader, VertexSource);
        uint frag = CompileShader(ShaderType.FragmentShader, FragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vert);
        _gl.AttachShader(Handle, frag);
        _gl.LinkProgram(Handle);

        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"[BlitShader] link failed: {info}");
        }
        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uProjection");
        if (loc < 0) return;
        unsafe { _gl.UniformMatrix4(loc, 1, false, (float*)&matrix); }
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"[BlitShader] {type} compile failed: {info}");
        }
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    private const string VertexSource = @"
#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;

out vec2 Frag_TexCoord;
uniform mat4 uProjection;

void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    Frag_TexCoord = aTexCoord;
}";

    private const string FragmentSource = @"
#version 330 core
in vec2 Frag_TexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;

void main() {
    FragColor = texture(uTexture, Frag_TexCoord);
}";
}
