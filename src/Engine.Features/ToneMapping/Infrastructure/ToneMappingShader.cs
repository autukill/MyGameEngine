namespace GameEngine.Features.ToneMapping.Infrastructure;

using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.ToneMapping.Domain;
using Silk.NET.OpenGL;
using System.Numerics;

public sealed class ToneMappingShader : IShader
{
    private readonly GL _gl;
    public uint Handle { get; }

    public ToneMappingShader(GL gl)
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
            throw new InvalidOperationException($"[ToneMappingShader] link failed: {gl.GetProgramInfoLog(Handle)}");
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
        Use();
        SetInt("uScene", 0);
        SetInt("uBloom", 1);
    }

    public void Use() => _gl.UseProgram(Handle);
    public void SetProjection(Matrix4x4 matrix) { }

    public void SetSettings(ToneMappingSettings settings, bool hasBloom)
    {
        Use();
        SetFloat("uExposure", settings.Exposure);
        SetFloat("uGamma", settings.Gamma);
        SetInt("uOperator", settings.Operator == ToneMappingOperator.Aces ? 0 : 1);
        SetInt("uHasBloom", hasBloom ? 1 : 0);
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
                $"[ToneMappingShader] {type} compile failed: {_gl.GetShaderInfoLog(shader)}");
        return shader;
    }

    private void SetFloat(string name, float value)
    {
        int location = _gl.GetUniformLocation(Handle, name);
        if (location >= 0) _gl.Uniform1(location, value);
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
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);
    vUv = aTexCoord;
}";

    private const string FragmentSource = @"
#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform float uExposure;
uniform float uGamma;
uniform int uOperator;
uniform bool uHasBloom;

vec3 aces(vec3 value) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((value * (a * value + b)) / (value * (c * value + d) + e), 0.0, 1.0);
}

void main() {
    vec3 hdr = texture(uScene, vUv).rgb;
    if (uHasBloom) hdr += texture(uBloom, vUv).rgb;
    hdr *= exp2(uExposure);
    vec3 mapped = uOperator == 0 ? aces(hdr) : hdr / (hdr + vec3(1.0));
    mapped = pow(clamp(mapped, 0.0, 1.0), vec3(1.0 / uGamma));
    FragColor = vec4(mapped, 1.0);
}";
}
