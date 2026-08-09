namespace GameEngine.Core.Infrastructure.Graphics;

using System.Text.RegularExpressions;

/// <summary>保留编译阶段与驱动日志的 Shader 构建错误。</summary>
public sealed class ShaderBuildException : InvalidOperationException
{
    public ShaderBuildException(
        string shaderName,
        string stage,
        string log,
        string? sourcePath = null)
        : base(CreateMessage(shaderName, stage, log, sourcePath, out int? sourceLine))
    {
        ShaderName = shaderName;
        Stage = stage;
        Log = log;
        SourcePath = sourcePath;
        SourceLine = sourceLine;
    }

    public string ShaderName { get; }
    public string Stage { get; }
    public string Log { get; }
    public string? SourcePath { get; }
    public int? SourceLine { get; }

    private static string CreateMessage(
        string shaderName,
        string stage,
        string log,
        string? sourcePath,
        out int? sourceLine)
    {
        sourceLine = TryParseFirstLine(log);
        string location = sourcePath is null
            ? string.Empty
            : sourceLine is { } line
                ? $" at '{sourcePath}':{line}"
                : $" at '{sourcePath}'";
        return $"Shader '{shaderName}' {stage} failed{location}: {log}";
    }

    private static int? TryParseFirstLine(string log)
    {
        if (string.IsNullOrWhiteSpace(log)) return null;
        Match match = Regex.Match(
            log,
            @"(?:ERROR:\s*)?\d+:(?<line>\d+)|\d+\((?<line>\d+)\)",
            RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["line"].Value, out int line)
            ? line
            : null;
    }
}
