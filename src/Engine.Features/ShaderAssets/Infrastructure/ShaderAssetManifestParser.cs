namespace GameEngine.Features.ShaderAssets.Infrastructure;

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Features.ShaderAssets.Domain;

public static class ShaderAssetManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ShaderAssetManifest Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ManifestDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(stream, JsonOptions) ??
                throw new InvalidDataException("Shader asset manifest cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Invalid shader asset manifest JSON: {exception.Message}", exception);
        }

        if (dto.SchemaVersion != 1)
            throw new InvalidDataException(
                $"Unsupported shader asset schemaVersion '{dto.SchemaVersion}'. Expected 1.");
        if (dto.Shaders is null || dto.Shaders.Length == 0)
            throw new InvalidDataException("Shader asset manifest must declare at least one shader.");
        if (dto.Materials is null)
            throw new InvalidDataException("Shader asset manifest materials array is required.");

        var shaders = new List<ShaderAssetDefinition>(dto.Shaders.Length);
        var shaderNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (ShaderDto shader in dto.Shaders)
        {
            RequireName(shader.Name, "Shader name");
            RequireRelativePath(shader.Vertex, $"Shader '{shader.Name}' vertex path");
            RequireRelativePath(shader.Fragment, $"Shader '{shader.Name}' fragment path");
            if (!shaderNames.Add(shader.Name!))
                throw new InvalidDataException($"Shader '{shader.Name}' is declared more than once.");
            shaders.Add(new ShaderAssetDefinition(
                shader.Name!, shader.Vertex!, shader.Fragment!));
        }

        var materials = new List<MaterialAssetDefinition>(dto.Materials.Length);
        var materialNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (MaterialDto material in dto.Materials)
        {
            RequireName(material.Name, "Material name");
            RequireName(material.Shader, $"Material '{material.Name}' shader");
            if (!materialNames.Add(material.Name!))
                throw new InvalidDataException(
                    $"Material '{material.Name}' is declared more than once.");
            if (!shaderNames.Contains(material.Shader!))
                throw new InvalidDataException(
                    $"Material '{material.Name}' references unknown shader '{material.Shader}'.");
            if (material.Uniforms is null)
                throw new InvalidDataException(
                    $"Material '{material.Name}' uniforms array is required.");

            var uniforms = new List<MaterialUniformAssetDefinition>(material.Uniforms.Length);
            var uniformNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (UniformDto uniform in material.Uniforms)
            {
                RequireName(uniform.Name, $"Material '{material.Name}' uniform name");
                if (!uniformNames.Add(uniform.Name!))
                    throw new InvalidDataException(
                        $"Material '{material.Name}' uniform '{uniform.Name}' is declared more than once.");
                ShaderUniformType type = ParseType(uniform.Type, material.Name!, uniform.Name!);
                ShaderUniformDefinition definition;
                try
                {
                    definition = new ShaderUniformDefinition(uniform.Name!, type);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException(exception.Message, exception);
                }
                uniforms.Add(new MaterialUniformAssetDefinition(
                    definition,
                    ParseDefault(uniform.Default, type, material.Name!, uniform.Name!)));
            }
            materials.Add(new MaterialAssetDefinition(
                material.Name!, material.Shader!, uniforms.AsReadOnly()));
        }

        return new ShaderAssetManifest(
            1,
            shaders.AsReadOnly(),
            materials.AsReadOnly());
    }

    private static ShaderUniformType ParseType(string? value, string material, string uniform) =>
        value switch
        {
            "float" => ShaderUniformType.Float,
            "int" => ShaderUniformType.Int,
            "vector2" => ShaderUniformType.Vector2,
            "vector4" => ShaderUniformType.Vector4,
            _ => throw new InvalidDataException(
                $"Material '{material}' uniform '{uniform}' has unsupported type '{value}'.")
        };

    private static MaterialUniformDefaultValue ParseDefault(
        JsonElement value,
        ShaderUniformType type,
        string material,
        string uniform)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidDataException(
                $"Material '{material}' uniform '{uniform}' default is required.");
        try
        {
            return type switch
            {
                ShaderUniformType.Float => MaterialUniformDefaultValue.Float(
                    RequireFinite(value.GetSingle(), material, uniform)),
                ShaderUniformType.Int => MaterialUniformDefaultValue.Int(value.GetInt32()),
                ShaderUniformType.Vector2 => MaterialUniformDefaultValue.Vector2(
                    ReadVector2(value, material, uniform)),
                ShaderUniformType.Vector4 => MaterialUniformDefaultValue.Vector4(
                    ReadVector4(value, material, uniform)),
                _ => throw new InvalidDataException(
                    $"Material '{material}' uniform '{uniform}' type is unsupported.")
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException(
                $"Material '{material}' uniform '{uniform}' default does not match {type}.",
                exception);
        }
    }

    private static Vector2 ReadVector2(JsonElement value, string material, string uniform)
    {
        Vector2Dto vector = value.Deserialize<Vector2Dto>(JsonOptions) ??
            throw new InvalidDataException("Vector2 default cannot be null.");
        return new Vector2(
            RequireFinite(vector.X, material, uniform),
            RequireFinite(vector.Y, material, uniform));
    }

    private static Vector4 ReadVector4(JsonElement value, string material, string uniform)
    {
        Vector4Dto vector = value.Deserialize<Vector4Dto>(JsonOptions) ??
            throw new InvalidDataException("Vector4 default cannot be null.");
        return new Vector4(
            RequireFinite(vector.X, material, uniform),
            RequireFinite(vector.Y, material, uniform),
            RequireFinite(vector.Z, material, uniform),
            RequireFinite(vector.W, material, uniform));
    }

    private static float RequireFinite(float value, string material, string uniform) =>
        float.IsFinite(value)
            ? value
            : throw new InvalidDataException(
                $"Material '{material}' uniform '{uniform}' default must be finite.");

    private static void RequireName(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{description} cannot be empty.");
    }

    private static void RequireRelativePath(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            throw new InvalidDataException($"{description} must be a relative path.");
    }

    private sealed record ManifestDto(
        int SchemaVersion,
        ShaderDto[]? Shaders,
        MaterialDto[]? Materials);

    private sealed record ShaderDto(string? Name, string? Vertex, string? Fragment);
    private sealed record MaterialDto(string? Name, string? Shader, UniformDto[]? Uniforms);
    private sealed record UniformDto(string? Name, string? Type, JsonElement Default);
    private sealed record Vector2Dto(float X, float Y);
    private sealed record Vector4Dto(float X, float Y, float Z, float W);
}
