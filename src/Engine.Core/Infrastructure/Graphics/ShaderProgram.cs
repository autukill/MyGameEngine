namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using Silk.NET.OpenGL;

/// <summary>支持原子 Program Handle 替换与 uniform location 缓存失效的通用 Shader。</summary>
public sealed class ShaderProgram : IShader
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);
    private bool _disposed;

    public uint Handle { get; private set; }
    public string Name { get; }

    internal ShaderProgram(
        GL gl,
        string name,
        string vertexSource,
        string fragmentSource,
        string? vertexPath = null,
        string? fragmentPath = null)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Shader name cannot be empty.", nameof(name));
        Name = name;
        Handle = CompileHandle(
            gl, name, vertexSource, fragmentSource, vertexPath, fragmentPath);
    }

    public void Use()
    {
        ThrowIfDisposed();
        _gl.UseProgram(Handle);
    }

    public void SetProjection(Matrix4x4 matrix)
    {
        Use();
        int location = Location("uProjection");
        if (location < 0) return;
        unsafe { _gl.UniformMatrix4(location, 1, false, (float*)&matrix); }
    }

    public void SetFloat(string name, float value)
    {
        Use();
        SetFloatBound(name, value);
    }

    public void SetVec2(string name, Vector2 value)
    {
        Use();
        SetVec2Bound(name, value);
    }

    public void SetVec4(string name, Vector4 value)
    {
        Use();
        SetVec4Bound(name, value);
    }

    public void SetInt(string name, int value)
    {
        Use();
        SetIntBound(name, value);
    }

    internal void SetFloatBound(string name, float value)
    {
        int location = Location(name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    internal void SetVec2Bound(string name, Vector2 value)
    {
        int location = Location(name);
        if (location >= 0) _gl.Uniform2(location, value.X, value.Y);
    }

    internal void SetVec4Bound(string name, Vector4 value)
    {
        int location = Location(name);
        if (location >= 0) _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    internal void SetIntBound(string name, int value)
    {
        int location = Location(name);
        if (location >= 0) _gl.Uniform1(location, value);
    }

    public void SetTexture(string name, int textureUnit) => SetInt(name, textureUnit);

    internal uint Activate(uint nextHandle)
    {
        ThrowIfDisposed();
        if (nextHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(nextHandle));
        uint previous = Handle;
        Handle = nextHandle;
        _locations.Clear();
        return previous;
    }

    internal static uint CompileHandle(
        GL gl,
        string name,
        string vertexSource,
        string fragmentSource,
        string? vertexPath = null,
        string? fragmentPath = null)
    {
        ArgumentNullException.ThrowIfNull(gl);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Shader name cannot be empty.", nameof(name));
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentSource);

        uint vertex = 0;
        uint fragment = 0;
        uint program = 0;
        try
        {
            vertex = CompileStage(
                gl, name, ShaderType.VertexShader, vertexSource, vertexPath);
            fragment = CompileStage(
                gl, name, ShaderType.FragmentShader, fragmentSource, fragmentPath);
            program = gl.CreateProgram();
            if (program == 0)
                throw new ShaderBuildException(name, "program creation", "The driver returned handle 0.");
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
                throw new ShaderBuildException(name, "link", gl.GetProgramInfoLog(program));

            gl.UseProgram(program);
            int texture = gl.GetUniformLocation(program, "uTexture");
            if (texture >= 0) gl.Uniform1(texture, 0);
            return program;
        }
        catch
        {
            if (program != 0) gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            if (vertex != 0) gl.DeleteShader(vertex);
            if (fragment != 0) gl.DeleteShader(fragment);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        uint handle = Handle;
        Handle = 0;
        _locations.Clear();
        if (handle != 0) _gl.DeleteProgram(handle);
    }

    internal static void ValidateMaterialContract(
        GL gl,
        uint handle,
        string shaderName,
        string materialName,
        MaterialParameterBlock parameters)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(parameters);
        gl.GetProgram(handle, ProgramPropertyARB.ActiveUniforms, out int count);
        var active = new Dictionary<string, (UniformType Type, int Size, bool IsArray)>(
            count,
            StringComparer.Ordinal);
        for (uint index = 0; index < count; index++)
        {
            string name = gl.GetActiveUniform(handle, index, out int size, out UniformType type);
            bool isArray = name.EndsWith("[0]", StringComparison.Ordinal);
            if (isArray)
                name = name[..^3];
            active[name] = (type, size, isArray);
        }

        List<ShaderUniformContractIssue>? issues = null;
        foreach (ShaderUniformDefinition expected in parameters.Uniforms)
        {
            if (!active.TryGetValue(expected.Name, out var actual))
            {
                (issues ??= []).Add(new ShaderUniformContractIssue(
                    expected.Name,
                    expected.Type,
                    null,
                    ShaderContractIssueKind.MissingUniform));
                continue;
            }
            if (actual.IsArray || actual.Size != 1)
            {
                (issues ??= []).Add(new ShaderUniformContractIssue(
                    expected.Name,
                    expected.Type,
                    $"{actual.Type}[{actual.Size}]",
                    ShaderContractIssueKind.ArrayUnsupported));
                continue;
            }
            UniformType required = expected.Type switch
            {
                ShaderUniformType.Float => UniformType.Float,
                ShaderUniformType.Int => UniformType.Int,
                ShaderUniformType.Vector2 => UniformType.FloatVec2,
                ShaderUniformType.Vector4 => UniformType.FloatVec4,
                _ => throw new InvalidOperationException(
                    $"Unsupported material uniform type '{expected.Type}'.")
            };
            if (actual.Type != required)
            {
                (issues ??= []).Add(new ShaderUniformContractIssue(
                    expected.Name,
                    expected.Type,
                    actual.Type.ToString(),
                    ShaderContractIssueKind.TypeMismatch));
            }
        }

        if (issues is not null)
            throw new ShaderContractException(shaderName, materialName, issues);
    }

    private static uint CompileStage(
        GL gl,
        string name,
        ShaderType type,
        string source,
        string? sourcePath)
    {
        uint shader = gl.CreateShader(type);
        if (shader == 0)
            throw new ShaderBuildException(
                name, type.ToString(), "The driver returned handle 0.", sourcePath);
        try
        {
            gl.ShaderSource(shader, source);
            gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0)
                throw new ShaderBuildException(
                    name, type.ToString(), gl.GetShaderInfoLog(shader), sourcePath);
            return shader;
        }
        catch
        {
            gl.DeleteShader(shader);
            throw;
        }
    }

    private int Location(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_locations.TryGetValue(name, out int location)) return location;
        location = _gl.GetUniformLocation(Handle, name);
        _locations.Add(name, location);
        return location;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
