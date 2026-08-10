namespace GameEngine.Features.TransformHierarchy.Gameplay;

using GameEngine.Features.TransformHierarchy.Domain;

/// <summary>Construction-only builder for one TransformPrefab instance.</summary>
public sealed class TransformPrefabBuilder
{
    private readonly TransformBindingBehavior _binding;
    private bool _frozen;

    internal TransformPrefabBuilder(TransformBindingBehavior binding) => _binding = binding;

    public TransformNodeRef<TTag> Attachment<TTag>(
        string name,
        in LocalTransform2D localTransform)
    {
        EnsureMutable();
        return new TransformNodeRef<TTag>(
            _binding.CreateAttachment(name, localTransform));
    }

    public TransformNodeRef<TTag> Attachment<TTag, TParentTag>(
        string name,
        in LocalTransform2D localTransform,
        TransformNodeRef<TParentTag> parent)
    {
        EnsureMutable();
        if (parent.IsEmpty)
            throw new ArgumentException("Parent transform node reference is empty.", nameof(parent));
        return new TransformNodeRef<TTag>(
            _binding.CreateAttachment(parent.Anchor, name, localTransform));
    }

    internal void Freeze() => _frozen = true;

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException(
                "Transform Prefab builders can only be used during their assembly callback.");
    }
}
