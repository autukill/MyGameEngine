namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 通用 2D Sprite Shader：Position+UV+Color → Texture * Color
/// 通过 SetProjection() 注入正交投影矩阵，支持相机变换。
/// </summary>
public class SpriteShader : IShader
{
    private readonly GL _gl;
    public uint Handle { get; }

    public SpriteShader(GL gl)
    {
        _gl = gl;

        uint vert = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint frag = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vert);
        _gl.AttachShader(Handle, frag);
        _gl.LinkProgram(Handle);

        // 检查链接状态
        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
        {
            string info = _gl.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"[SpriteShader] link failed: {info}");
        }

        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);

        // 显式设 uTexture sampler = 0
        Use();
        int texLoc = _gl.GetUniformLocation(Handle, "uTexture");
        if (texLoc >= 0) _gl.Uniform1(texLoc, 0);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uProjection");
        if (loc < 0) return;
        unsafe
        {
            _gl.UniformMatrix4(loc, 1, false, (float*)&matrix);
        }
    }

    /// <summary>设置 iTime/uTime uniform（用于后处理 Shader）</summary>
    public void SetFloat(string name, float value)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, name);
        if (loc >= 0) _gl.Uniform1(loc, value);
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
            throw new InvalidOperationException($"[SpriteShader] compile {type} failed: {info}");
        }
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

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
}
