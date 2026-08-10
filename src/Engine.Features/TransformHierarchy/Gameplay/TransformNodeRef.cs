namespace GameEngine.Features.TransformHierarchy.Gameplay;

using System.Numerics;
using GameEngine.Features.TransformHierarchy.Domain;

/// <summary>
/// Strongly typed reference to a named node declared by a TransformPrefab. TTag is an authoring
/// marker and has no runtime allocation or inheritance requirements.
/// </summary>
public readonly struct TransformNodeRef<TTag>
{
    private readonly TransformAnchor? _anchor;

    internal TransformNodeRef(TransformAnchor anchor) => _anchor = anchor;

    public bool IsEmpty => _anchor is null;
    public string Name => Require().Name;
    public bool IsReady => _anchor is { IsReady: true };
    public LocalTransform2D LocalTransform
    {
        get => Require().LocalTransform;
        set => Require().LocalTransform = value;
    }
    public Vector2 LocalPosition
    {
        get => Require().LocalPosition;
        set => Require().LocalPosition = value;
    }
    public Matrix3x2 WorldMatrix => Require().WorldMatrix;
    public Vector2 WorldPosition => Require().WorldPosition;

    public Vector2 TransformPointToWorld(Vector2 localPoint) =>
        Require().TransformPointToWorld(localPoint);

    /// <summary>Queues a GameInstance binding under this typed Prefab node.</summary>
    public void Attach(
        TransformBindingBehavior child,
        TransformReparentMode mode = TransformReparentMode.KeepLocal)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Anchor.AttachTo(Require(), mode);
    }

    internal TransformAnchor Anchor => Require();

    private TransformAnchor Require() => _anchor ??
        throw new InvalidOperationException("Transform node reference is empty.");
}
