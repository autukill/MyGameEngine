namespace GameEngine.Core.Infrastructure.Graphics;

/// <summary>保留编译阶段与驱动日志的 Shader 构建错误。</summary>
public sealed class ShaderBuildException : InvalidOperationException
{
    public ShaderBuildException(string shaderName, string stage, string log)
        : base($"Shader '{shaderName}' {stage} failed: {log}")
    {
        ShaderName = shaderName;
        Stage = stage;
        Log = log;
    }

    public string ShaderName { get; }
    public string Stage { get; }
    public string Log { get; }
}
