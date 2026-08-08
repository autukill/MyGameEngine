namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using System.Numerics;

/// <summary>
/// 通用 ShaderProgram：编译 GLSL 源码（字符串），提供通用 uniform 设置（location 缓存，零 GC）。
///
/// 取代"每个效果手写一个 Shader 类 + 手写 SetXxx uniform 方法"的样板：
///   新增 shader = 写一份 GLSL + ShaderLibrary.Create 一行注册。
/// 高频/多参数 shader（如 SpriteShader）仍可保留专用类派生/并行。
/// </summary>
public sealed class ShaderProgram : IShader
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = new();

    public uint Handle { get; }
    public string Name { get; }

    internal ShaderProgram(GL gl, string name, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        Name = name;

        uint vert = CompileShader(ShaderType.VertexShader, vertexSource);
        uint frag = CompileShader(ShaderType.FragmentShader, fragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vert);
        _gl.AttachShader(Handle, frag);
        _gl.LinkProgram(Handle);

        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException(
                $"[ShaderProgram:{name}] link failed: {_gl.GetProgramInfoLog(Handle)}");

        _gl.DeleteShader(vert);
        _gl.DeleteShader(frag);

        // 显式设 uTexture sampler = 0（某些驱动不遵守 GLSL 默认值 0）
        Use();
        int texLoc = _gl.GetUniformLocation(Handle, "uTexture");
        if (texLoc >= 0) _gl.Uniform1(texLoc, 0);
    }

    public void Use() => _gl.UseProgram(Handle);

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int loc = Location("uProjection");
        if (loc < 0) return;
        unsafe
        {
            _gl.UniformMatrix4(loc, 1, false, (float*)&matrix);
        }
    }

    public void SetFloat(string name, float value)
    {
        Use();
        int loc = Location(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    public void SetVec2(string name, Vector2 value)
    {
        Use();
        int loc = Location(name);
        if (loc >= 0) _gl.Uniform2(loc, value.X, value.Y);
    }

    public void SetVec4(string name, Vector4 value)
    {
        Use();
        int loc = Location(name);
        if (loc >= 0) _gl.Uniform4(loc, value.X, value.Y, value.Z, value.W);
    }

    public void SetInt(string name, int value)
    {
        Use();
        int loc = Location(name);
        if (loc >= 0) _gl.Uniform1(loc, value);
    }

    /// <summary>设置 sampler uniform（texture unit 编号，通常传 0）</summary>
    public void SetTexture(string name, int textureUnit)
    {
        Use();
        int loc = Location(name);
        if (loc >= 0) _gl.Uniform1(loc, textureUnit);
    }

    /// <summary>uniform location 缓存：首次查询后零分配</summary>
    private int Location(string name)
    {
        if (_locations.TryGetValue(name, out int loc)) return loc;
        loc = _gl.GetUniformLocation(Handle, name);
        _locations[name] = loc;
        return loc;
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
            throw new InvalidOperationException($"[ShaderProgram:{Name}] {type} compile failed: {info}");
        }
        return shader;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
