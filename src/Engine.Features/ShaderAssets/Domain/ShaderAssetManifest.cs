namespace GameEngine.Features.ShaderAssets.Domain;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;

public sealed record ShaderAssetManifest(
    int SchemaVersion,
    IReadOnlyList<ShaderAssetDefinition> Shaders,
    IReadOnlyList<MaterialAssetDefinition> Materials);

public sealed record ShaderAssetDefinition(
    string Name,
    string VertexPath,
    string FragmentPath);

public sealed record MaterialAssetDefinition(
    string Name,
    string Shader,
    IReadOnlyList<MaterialUniformAssetDefinition> Uniforms);

public sealed record MaterialUniformAssetDefinition(
    ShaderUniformDefinition Uniform,
    MaterialUniformDefaultValue DefaultValue);

public readonly record struct MaterialUniformDefaultValue
{
    private MaterialUniformDefaultValue(
        ShaderUniformType type,
        float floatValue,
        int intValue,
        Vector2 vector2Value,
        Vector4 vector4Value)
    {
        Type = type;
        FloatValue = floatValue;
        IntValue = intValue;
        Vector2Value = vector2Value;
        Vector4Value = vector4Value;
    }

    public ShaderUniformType Type { get; }
    public float FloatValue { get; }
    public int IntValue { get; }
    public Vector2 Vector2Value { get; }
    public Vector4 Vector4Value { get; }

    public static MaterialUniformDefaultValue Float(float value) =>
        new(ShaderUniformType.Float, value, 0, default, default);

    public static MaterialUniformDefaultValue Int(int value) =>
        new(ShaderUniformType.Int, 0, value, default, default);

    public static MaterialUniformDefaultValue Vector2(Vector2 value) =>
        new(ShaderUniformType.Vector2, 0, 0, value, default);

    public static MaterialUniformDefaultValue Vector4(Vector4 value) =>
        new(ShaderUniformType.Vector4, 0, 0, default, value);
}
