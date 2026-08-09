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

    public ShaderMaterial Set(MaterialParameterRef<float> parameter, float value)
    {
        RequireOwner(parameter.Material);
        return SetFloat(parameter.Name, value);
    }

    public ShaderMaterial Set(MaterialParameterRef<int> parameter, int value)
    {
        RequireOwner(parameter.Material);
        return SetInt(parameter.Name, value);
    }

    public ShaderMaterial Set(MaterialParameterRef<Vector2> parameter, Vector2 value)
    {
        RequireOwner(parameter.Material);
        return SetVector2(parameter.Name, value);
    }

    public ShaderMaterial Set(MaterialParameterRef<Vector4> parameter, Vector4 value)
    {
        RequireOwner(parameter.Material);
        return SetVector4(parameter.Name, value);
    }

    private void RequireOwner(MaterialRef material)
    {
        if (material != Ref)
        {
            throw new ArgumentException(
                $"Material parameter belongs to '{material.Name}', not '{Ref.Name}'.",
                nameof(material));
        }
    }
}
