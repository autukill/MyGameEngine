namespace GameEngine.Features.StencilMasking.Infrastructure;

using System.Numerics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.StencilMasking.Domain;
using Silk.NET.OpenGL;

/// <summary>Circle 使用局部 UV，SpriteAlpha 使用当前帧纹理 Alpha 决定 fragment 是否写 Stencil。</summary>
public sealed class StencilMaskShader : IShader
{
    private readonly GL _gl;
    public uint Handle { get; }

    public StencilMaskShader(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        uint vertex = Compile(ShaderType.VertexShader, VertexSource);
        uint fragment = Compile(ShaderType.FragmentShader, FragmentSource);
        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vertex);
        gl.AttachShader(Handle, fragment);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException(
                $"[StencilMaskShader] link failed: {gl.GetProgramInfoLog(Handle)}");
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        Use();
        SetInt("uTexture", 0);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int location = _gl.GetUniformLocation(Handle, "uProjection");
        if (location >= 0)
        {
            unsafe { _gl.UniformMatrix4(location, 1, false, (float*)&matrix); }
        }
    }

    public void SetGeometry(StencilMaskGeometryKind kind, float alphaCutoff = 0.5f)
    {
        Use();
        SetInt("uMaskKind", kind == StencilMaskGeometryKind.Circle ? 0 : 1);
        int cutoff = _gl.GetUniformLocation(Handle, "uAlphaCutoff");
        if (cutoff >= 0) _gl.Uniform1(cutoff, alphaCutoff);
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException(
                $"[StencilMaskShader] {type} compile failed: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private void SetInt(string name, int value)
    {
        int location = _gl.GetUniformLocation(Handle, name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    private const string VertexSource = @"
#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;
out vec2 vUv;
uniform mat4 uProjection;
void main() {
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vUv = aTexCoord;
}";

    private const string FragmentSource = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform int uMaskKind;
uniform float uAlphaCutoff;
void main() {
    if (uMaskKind == 0) {
        vec2 p = vUv * 2.0 - 1.0;
        if (dot(p, p) > 1.0) discard;
    } else {
        if (texture(uTexture, vUv).a < uAlphaCutoff) discard;
    }
    FragColor = vec4(1.0);
}";
}
