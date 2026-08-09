namespace GameEngine.Core.Domain.Graphics;

/// <summary>Declares one named, type-safe material uniform.</summary>
public readonly record struct ShaderUniformDefinition
{
    public string Name { get; }
    public ShaderUniformType Type { get; }

    public ShaderUniformDefinition(string name, ShaderUniformType type)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Uniform name cannot be empty.", nameof(name));
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (name is "uProjection" or "uTexture")
            throw new ArgumentException(
                $"Uniform '{name}' is owned by the engine and cannot be a material parameter.",
                nameof(name));
        Name = name;
        Type = type;
    }

    public static ShaderUniformDefinition Float(string name) =>
        new(name, ShaderUniformType.Float);

    public static ShaderUniformDefinition Int(string name) =>
        new(name, ShaderUniformType.Int);

    public static ShaderUniformDefinition Vector2(string name) =>
        new(name, ShaderUniformType.Vector2);

    public static ShaderUniformDefinition Vector4(string name) =>
        new(name, ShaderUniformType.Vector4);
}
