namespace GameEngine.Core.Infrastructure.Graphics;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;

/// <summary>A named material instance backed by a logical shader and typed CPU parameters.</summary>
public sealed class ShaderMaterial
{
    internal ShaderMaterial(MaterialRef reference, ShaderRef shader, MaterialParameterBlock parameters)
    {
        Ref = reference;
        Shader = shader;
        Parameters = parameters;
    }

    public MaterialRef Ref { get; }
    public ShaderRef Shader { get; }
    public MaterialParameterBlock Parameters { get; }

    public ShaderMaterial SetFloat(string name, float value)
    {
        Parameters.SetFloat(name, value);
        return this;
    }

    public ShaderMaterial SetInt(string name, int value)
    {
        Parameters.SetInt(name, value);
        return this;
    }

    public ShaderMaterial SetVector2(string name, Vector2 value)
    {
        Parameters.SetVector2(name, value);
        return this;
    }

    public ShaderMaterial SetVector4(string name, Vector4 value)
    {
        Parameters.SetVector4(name, value);
        return this;
    }
}
