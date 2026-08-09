namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using Silk.NET.OpenGL;

/// <summary>拥有逻辑 ShaderRef 对应的 Program，并支持整批编译成功后原子替换。</summary>
public sealed class ShaderLibrary : IShaderResolver, IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, ShaderProgram> _programs = new(StringComparer.Ordinal);
    private bool _disposed;

    public ShaderLibrary(GL gl) => _gl = gl ?? throw new ArgumentNullException(nameof(gl));

    public int Count => _programs.Count;

    public ShaderProgram Create(string name, string vertexSource, string fragmentSource)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Shader name cannot be empty.", nameof(name));
        if (_programs.ContainsKey(name))
            throw new ArgumentException($"Shader '{name}' is already registered.", nameof(name));
        var program = new ShaderProgram(_gl, name, vertexSource, fragmentSource);
        _programs.Add(name, program);
        return program;
    }

    public ShaderProgram? TryGet(string name)
    {
        ThrowIfDisposed();
        return _programs.TryGetValue(name, out ShaderProgram? program) ? program : null;
    }

    public uint Resolve(ShaderRef shader)
    {
        ThrowIfDisposed();
        if (shader.IsEmpty) return 0;
        return _programs.TryGetValue(shader.Name, out ShaderProgram? program)
            ? program.Handle
            : 0;
    }

    public void SetProjection(Matrix4x4 projection)
    {
        ThrowIfDisposed();
        foreach (ShaderProgram program in _programs.Values)
            program.SetProjection(projection);
    }

    /// <summary>先编译全部候选 Program；任意失败时删除候选并保留所有旧 Handle。</summary>
    public void ReplaceAll(IReadOnlyList<ShaderProgramSource> replacements)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(replacements);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderProgramSource replacement in replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            if (!names.Add(replacement.Name))
                throw new ArgumentException($"Shader '{replacement.Name}' appears more than once.", nameof(replacements));
            if (!_programs.TryGetValue(replacement.Name, out ShaderProgram? program))
                throw new KeyNotFoundException($"Shader '{replacement.Name}' is not registered.");
        }

        var staged = new List<(ShaderProgram Program, uint Handle)>(replacements.Count);
        try
        {
            foreach (ShaderProgramSource replacement in replacements)
            {
                ShaderProgram program = _programs[replacement.Name];
                staged.Add((program, ShaderProgram.CompileHandle(
                    _gl,
                    replacement.Name,
                    replacement.VertexSource,
                    replacement.FragmentSource)));
            }
        }
        catch
        {
            foreach (var candidate in staged) _gl.DeleteProgram(candidate.Handle);
            throw;
        }

        var previous = new uint[staged.Count];
        for (int i = 0; i < staged.Count; i++)
            previous[i] = staged[i].Program.Activate(staged[i].Handle);
        foreach (uint handle in previous)
            _gl.DeleteProgram(handle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ShaderProgram program in _programs.Values) program.Dispose();
        _programs.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
