namespace GameEngine.Core.Domain.Graphics;

/// <summary>一次 Shader Program 编译所需的纯文本源码。</summary>
public sealed record ShaderProgramSource(
    string Name,
    string VertexSource,
    string FragmentSource,
    string? VertexPath = null,
    string? FragmentPath = null);
