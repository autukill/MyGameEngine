namespace GameEngine.Core.Infrastructure.Graphics;

using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Graphics;

/// <summary>
/// Shader 库：注册/解析 ShaderRef，实现 IShaderResolver。
///
/// 生命周期 = 组合根（Program）创建与释放；Pass/实例只"借用" program handle，不持有 GL 对象。
/// 未知名字 Resolve 返回 0（= 使用默认 shader），避免实例崩溃。
/// </summary>
public sealed class ShaderLibrary : IShaderResolver, IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, ShaderProgram> _programs = new();

    public ShaderLibrary(GL gl) => _gl = gl;

    /// <summary>编译并注册一个 shader（新增 shader 的唯一入口）</summary>
    public ShaderProgram Create(string name, string vertexSource, string fragmentSource)
    {
        var program = new ShaderProgram(_gl, name, vertexSource, fragmentSource);
        _programs[name] = program;
        return program;
    }

    public ShaderProgram? TryGet(string name) =>
        _programs.TryGetValue(name, out var p) ? p : null;

    /// <summary>IShaderResolver：ShaderRef → program handle（未知/空 → 0）</summary>
    public uint Resolve(ShaderRef shader)
    {
        if (shader.IsEmpty) return 0;
        return _programs.TryGetValue(shader.Name, out var p) ? p.Handle : 0;
    }

    public void Dispose()
    {
        foreach (var p in _programs.Values)
            p.Dispose();
        _programs.Clear();
    }
}
