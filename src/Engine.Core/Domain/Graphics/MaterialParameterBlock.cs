namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;

/// <summary>
/// CPU-side material parameters. Values survive shader program replacement because no GL handles
/// or uniform locations are stored here. Mutate during Step or before submitting the material.
/// </summary>
public sealed class MaterialParameterBlock
{
    private readonly ShaderUniformDefinition[] _uniforms;
    private readonly IReadOnlyList<ShaderUniformDefinition> _readOnlyUniforms;
    private readonly ParameterValue[] _values;
    private readonly Dictionary<string, int> _indices;

    public MaterialParameterBlock(params ShaderUniformDefinition[] uniforms)
    {
        ArgumentNullException.ThrowIfNull(uniforms);
        _uniforms = uniforms.ToArray();
        _values = new ParameterValue[_uniforms.Length];
        _indices = new Dictionary<string, int>(_uniforms.Length, StringComparer.Ordinal);
        for (int i = 0; i < _uniforms.Length; i++)
        {
            ShaderUniformDefinition supplied = _uniforms[i];
            var uniform = new ShaderUniformDefinition(supplied.Name, supplied.Type);
            _uniforms[i] = uniform;
            if (!_indices.TryAdd(uniform.Name, i))
                throw new ArgumentException(
                    $"Uniform '{uniform.Name}' is declared more than once.", nameof(uniforms));
        }
        _readOnlyUniforms = Array.AsReadOnly(_uniforms);
    }

    public IReadOnlyList<ShaderUniformDefinition> Uniforms => _readOnlyUniforms;

    /// <summary>Changes only when a parameter value actually changes.</summary>
    public long Revision { get; private set; }

    public MaterialParameterBlock SetFloat(string name, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Uniform value must be finite.");
        int index = Require(name, ShaderUniformType.Float);
        if (_values[index].Float == value) return this;
        _values[index].Float = value;
        Revision++;
        return this;
    }

    public MaterialParameterBlock SetInt(string name, int value)
    {
        int index = Require(name, ShaderUniformType.Int);
        if (_values[index].Int == value) return this;
        _values[index].Int = value;
        Revision++;
        return this;
    }

    public MaterialParameterBlock SetVector2(string name, Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            throw new ArgumentOutOfRangeException(nameof(value), "Uniform value must be finite.");
        int index = Require(name, ShaderUniformType.Vector2);
        if (_values[index].Vector2 == value) return this;
        _values[index].Vector2 = value;
        Revision++;
        return this;
    }

    public MaterialParameterBlock SetVector4(string name, Vector4 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new ArgumentOutOfRangeException(nameof(value), "Uniform value must be finite.");
        int index = Require(name, ShaderUniformType.Vector4);
        if (_values[index].Vector4 == value) return this;
        _values[index].Vector4 = value;
        Revision++;
        return this;
    }

    public float GetFloat(string name) => _values[Require(name, ShaderUniformType.Float)].Float;
    public int GetInt(string name) => _values[Require(name, ShaderUniformType.Int)].Int;
    public Vector2 GetVector2(string name) =>
        _values[Require(name, ShaderUniformType.Vector2)].Vector2;
    public Vector4 GetVector4(string name) =>
        _values[Require(name, ShaderUniformType.Vector4)].Vector4;

    internal float GetFloat(int index) => _values[index].Float;
    internal int GetInt(int index) => _values[index].Int;
    internal Vector2 GetVector2(int index) => _values[index].Vector2;
    internal Vector4 GetVector4(int index) => _values[index].Vector4;

    private int Require(string name, ShaderUniformType expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_indices.TryGetValue(name, out int index))
            throw new KeyNotFoundException($"Material uniform '{name}' is not declared.");
        ShaderUniformType actual = _uniforms[index].Type;
        if (actual != expected)
            throw new InvalidOperationException(
                $"Material uniform '{name}' is {actual}, not {expected}.");
        return index;
    }

    private struct ParameterValue
    {
        public float Float;
        public int Int;
        public Vector2 Vector2;
        public Vector4 Vector4;
    }
}
