namespace GameEngine.Features.TransformHierarchy.Gameplay;

using GameEngine.Core.Domain.Entities;

public delegate TParts TransformPrefabAssembler<TParts>(TransformPrefabBuilder builder);

/// <summary>
/// Immutable, reusable declaration of a GameInstance root and its typed pure transform parts.
/// The definition owns no Scene state; Instantiate creates owner-local logical nodes.
/// </summary>
public sealed class TransformPrefab<TParts>
{
    private readonly TransformPrefabAssembler<TParts> _assemble;

    public TransformPrefab(string name, TransformPrefabAssembler<TParts> assemble)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assemble);
        Name = name;
        _assemble = assemble;
    }

    public string Name { get; }

    public TransformPrefabInstance<TParts> Instantiate(
        GameInstance root,
        SceneTransformRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(runtime);
        TransformBindingBehavior binding = root.UseTransformHierarchy(runtime);
        var builder = new TransformPrefabBuilder(binding);
        try
        {
            TParts parts = _assemble(builder);
            if (parts is null)
                throw new InvalidOperationException(
                    $"Transform Prefab '{Name}' returned null parts.");
            return new TransformPrefabInstance<TParts>(Name, binding, parts);
        }
        catch
        {
            binding.DiscardAuthoring();
            throw;
        }
        finally
        {
            builder.Freeze();
        }
    }
}

/// <summary>One owner-local instantiation of a reusable TransformPrefab definition.</summary>
public readonly record struct TransformPrefabInstance<TParts>(
    string Name,
    TransformBindingBehavior Root,
    TParts Parts);
