namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;

/// <summary>
/// A logical, strongly typed reference to one declared material parameter. It contains no GPU
/// handle or uniform location and remains stable when the underlying shader program is replaced.
/// </summary>
public readonly record struct MaterialParameterRef<T>
{
    public MaterialRef Material { get; }
    public string Name { get; }

    public MaterialParameterRef(MaterialRef material, string name)
    {
        if (material.IsEmpty)
            throw new ArgumentException("Material parameter owner cannot be empty.", nameof(material));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsSupportedType())
        {
            throw new NotSupportedException(
                $"Material parameter type '{typeof(T).FullName}' is not supported.");
        }

        Material = material;
        Name = name;
    }

    public bool IsEmpty => Material.IsEmpty || string.IsNullOrEmpty(Name);

    public override string ToString() => $"{Material.Name}.{Name}";

    private static bool IsSupportedType() =>
        typeof(T) == typeof(float) ||
        typeof(T) == typeof(int) ||
        typeof(T) == typeof(Vector2) ||
        typeof(T) == typeof(Vector4);
}
