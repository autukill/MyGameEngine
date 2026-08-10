namespace GameEngine.Features.TransformHierarchy.Gameplay;

using GameEngine.Core.Domain.Entities;

public static class TransformHierarchyExtensions
{
    /// <summary>Opts a GameInstance into scene-scoped parent/child transform authoring.</summary>
    public static TransformBindingBehavior UseTransformHierarchy(
        this GameInstance instance,
        SceneTransformRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(runtime);
        if (instance.FindBehavior<TransformBindingBehavior>() is not null)
        {
            throw new InvalidOperationException(
                $"GameInstance '{instance.GetType().Name}' already has a transform hierarchy binding.");
        }
        return instance.UseBehavior(new TransformBindingBehavior(runtime));
    }
}
