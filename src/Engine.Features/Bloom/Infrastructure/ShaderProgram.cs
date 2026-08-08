namespace GameEngine.Features.Bloom.Infrastructure;

using Silk.NET.OpenGL;

internal static class ShaderProgram
{
    public static uint Create(GL gl, string vertexSource, string fragmentSource, string name)
    {
        uint vertex = 0;
        uint fragment = 0;
        uint program = 0;
        try
        {
            vertex = Compile(gl, ShaderType.VertexShader, vertexSource, name);
            fragment = Compile(gl, ShaderType.FragmentShader, fragmentSource, name);
            program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
                throw new InvalidOperationException($"[{name}] link failed: {gl.GetProgramInfoLog(program)}");
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

    private static uint Compile(GL gl, ShaderType type, string source, string name)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != 0) return shader;
        string log = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new InvalidOperationException($"[{name}] {type} compile failed: {log}");
    }
}
