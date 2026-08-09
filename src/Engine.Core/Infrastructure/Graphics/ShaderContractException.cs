namespace GameEngine.Core.Infrastructure.Graphics;

using GameEngine.Core.Domain.Graphics;

public enum ShaderContractIssueKind
{
    MissingUniform,
    TypeMismatch,
    ArrayUnsupported
}

public sealed record ShaderUniformContractIssue(
    string UniformName,
    ShaderUniformType ExpectedType,
    string? ActualType,
    ShaderContractIssueKind Kind);

/// <summary>Raised before a material can use a linked Program with an incompatible uniform contract.</summary>
public sealed class ShaderContractException : InvalidOperationException
{
    public ShaderContractException(
        string shaderName,
        string materialName,
        IReadOnlyList<ShaderUniformContractIssue> issues)
        : base(CreateMessage(shaderName, materialName, issues))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialName);
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0)
            throw new ArgumentException("At least one contract issue is required.", nameof(issues));
        ShaderName = shaderName;
        MaterialName = materialName;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public string ShaderName { get; }
    public string MaterialName { get; }
    public IReadOnlyList<ShaderUniformContractIssue> Issues { get; }

    private static string CreateMessage(
        string shaderName,
        string materialName,
        IReadOnlyList<ShaderUniformContractIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        string details = string.Join("; ", issues.Select(issue => issue.Kind switch
        {
            ShaderContractIssueKind.MissingUniform =>
                $"'{issue.UniformName}' expected {issue.ExpectedType} but is missing or inactive",
            ShaderContractIssueKind.TypeMismatch =>
                $"'{issue.UniformName}' expected {issue.ExpectedType} but is {issue.ActualType}",
            ShaderContractIssueKind.ArrayUnsupported =>
                $"'{issue.UniformName}' expected one {issue.ExpectedType} value but is {issue.ActualType}",
            _ => $"'{issue.UniformName}' has an unknown contract issue"
        }));
        return $"Material '{materialName}' is incompatible with shader '{shaderName}': {details}.";
    }
}
