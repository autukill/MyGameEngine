namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 后处理 Shader：Bright Pass + 9-tap Gaussian Blur 近似
/// 用于 Demo 中的 Bloom 效果（单 Pass 简化版）。
/// 真实生产中应使用 2-pass 分离高斯（Horizontal + Vertical）。
/// </summary>
public sealed class PostProcessShader : IShader
{
    private readonly GL _gl;
    public uint Handle { get; }

    public PostProcessShader(GL gl)
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
            throw new InvalidOperationException($"[PostProcessShader] link failed: {info}");
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

    public void SetTextureSize(float width, float height)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uTextureSize");
        if (loc >= 0) _gl.Uniform2(loc, width, height);
    }

    public void SetBrightnessThreshold(float threshold)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uThreshold");
        if (loc >= 0) _gl.Uniform1(loc, threshold);
    }

    public void SetIntensity(float intensity)
    {
        Use();
        int loc = _gl.GetUniformLocation(Handle, "uIntensity");
        if (loc >= 0) _gl.Uniform1(loc, intensity);
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
            throw new InvalidOperationException($"[PostProcessShader] {type} compile failed: {info}");
        }
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);

    private const string VertexSource = @"
#version 330 core
// Fullscreen quad vertex shader: pass through position and UV.
// uProjection is set to an ortho matrix mapping screen pixels -> clip space.
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
// Post-process fragment shader: single-pass Bloom approximation.
// Combines bright-pass thresholding with a 9-tap Gaussian blur.
// Production-quality Bloom typically uses 2-pass separable Gaussian (H then V).

in vec2 Frag_TexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec2 uTextureSize;   // texture (width, height) in pixels
uniform float uThreshold;    // luminance threshold; pixels above this contribute to Bloom
uniform float uIntensity;    // Bloom additive intensity multiplier

// 9-tap Gaussian kernel weights (sum ~= 1.0).
// GLSL 330 requires explicit-size array constructor: float[9](...).
const float Weights[9] = float[9](
    0.0625, 0.09375, 0.125,
    0.15625, 0.1875, 0.15625,
    0.125, 0.09375, 0.0625
);

// 3x3 neighborhood offsets in texel units (-1, 0, +1 on each axis).
const vec2 Offsets[9] = vec2[9](
    vec2(-1.0, -1.0), vec2( 0.0, -1.0), vec2( 1.0, -1.0),
    vec2(-1.0,  0.0), vec2( 0.0,  0.0), vec2( 1.0,  0.0),
    vec2(-1.0,  1.0), vec2( 0.0,  1.0), vec2( 1.0,  1.0)
);

void main() {
    // Convert pixel offsets to UV-space deltas.
    vec2 texelSize = 1.0 / uTextureSize;
    vec3 sum = vec3(0.0);
    for (int i = 0; i < 9; i++) {
        vec3 c = texture(uTexture, Frag_TexCoord + Offsets[i] * texelSize).rgb;
        // Bright pass: only pixels whose luminance exceeds threshold accumulate.
        float brightness = dot(c, vec3(0.2126, 0.7152, 0.0722));
        if (brightness > uThreshold) {
            sum += c * Weights[i];
        }
    }
    sum *= uIntensity;
    FragColor = vec4(sum, 1.0);
}";
}
