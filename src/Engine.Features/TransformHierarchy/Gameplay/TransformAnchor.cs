namespace GameEngine.Features.TransformHierarchy.Gameplay;

using System.Numerics;
using GameEngine.Features.TransformHierarchy.Domain;

/// <summary>A stable logical transform point used by instances and lightweight attachments.</summary>
public sealed class TransformAnchor
{
    private LocalTransform2D _cachedLocal;
    private TransformNodeHandle _node;
    private TransformAnchor? _pendingParent;
    private TransformReparentMode _pendingMode;
    private bool _hasPendingParent;
    private bool _explicitLocalEdit;
    private bool _destroyed;

    internal TransformAnchor(
        SceneTransformRuntime runtime,
        long id,
        TransformBindingBehavior? binding,
        TransformBindingBehavior lifetimeOwner,
        in LocalTransform2D local,
        string? name)
    {
        Runtime = runtime;
        Id = id;
        Binding = binding;
        LifetimeOwner = lifetimeOwner;
        _cachedLocal = local;
        Name = name ?? $"instance-{id}";
    }

    public long Id { get; }
    public string Name { get; }
    public bool IsReady => !_node.IsEmpty && !_destroyed;
    public bool IsDestroyed => _destroyed;
    public LocalTransform2D LocalTransform
    {
        get => Runtime.GetLocal(this);
        set => Runtime.SetLocal(this, value);
    }
    public Vector2 LocalPosition
    {
        get => LocalTransform.Position;
        set => LocalTransform = LocalTransform with { Position = value };
    }
    public float LocalRotationRadians
    {
        get => LocalTransform.RotationRadians;
        set => LocalTransform = LocalTransform with { RotationRadians = value };
    }
    public Vector2 LocalScale
    {
        get => LocalTransform.Scale;
        set => LocalTransform = LocalTransform with { Scale = value };
    }
    public Matrix3x2 WorldMatrix => Runtime.GetWorldMatrix(this);
    public Vector2 WorldPosition => Runtime.GetWorldPosition(this);

    public Vector2 TransformPointToWorld(Vector2 localPoint) =>
        Runtime.TransformPointToWorld(this, localPoint);

    /// <summary>Declares a nested pure attachment with this node as its parent.</summary>
    public TransformAnchor CreateAttachment(
        string name,
        in LocalTransform2D localTransform) =>
        Runtime.CreateAttachment(LifetimeOwner, this, name, localTransform);

    /// <summary>Queues spatial parenting for the next Scene transform synchronization boundary.</summary>
    public void AttachTo(
        TransformAnchor parent,
        TransformReparentMode mode = TransformReparentMode.KeepLocal) =>
        Runtime.QueueParent(this, parent, mode);

    public void Detach(TransformReparentMode mode = TransformReparentMode.KeepLocal) =>
        Runtime.QueueParent(this, null, mode);

    /// <summary>Destroys a pure attachment. Bound instance anchors follow Scene lifecycle.</summary>
    public void Destroy() => Runtime.DestroyAttachment(this);

    internal SceneTransformRuntime Runtime { get; }
    internal TransformBindingBehavior? Binding { get; }
    internal TransformBindingBehavior LifetimeOwner { get; }
    internal TransformNodeHandle Node => _node;
    internal LocalTransform2D CachedLocal => _cachedLocal;
    internal bool HasPendingParent => _hasPendingParent;
    internal TransformAnchor? PendingParent => _pendingParent;
    internal TransformReparentMode PendingMode => _pendingMode;
    internal bool HasExplicitLocalEdit => _explicitLocalEdit;

    internal void Activate(TransformNodeHandle node) => _node = node;
    internal void Deactivate() => _node = TransformNodeHandle.None;
    internal void CacheLocal(in LocalTransform2D value)
    {
        _cachedLocal = value;
        _explicitLocalEdit = true;
    }
    internal void RefreshLocal(in LocalTransform2D value) => _cachedLocal = value;
    internal void ClearExplicitLocalEdit() => _explicitLocalEdit = false;
    internal void QueueParent(TransformAnchor? parent, TransformReparentMode mode)
    {
        _pendingParent = parent;
        _pendingMode = mode;
        _hasPendingParent = true;
    }
    internal void ClearPendingParent()
    {
        _pendingParent = null;
        _hasPendingParent = false;
    }
    internal void MarkDestroyed()
    {
        _destroyed = true;
        _node = TransformNodeHandle.None;
        ClearPendingParent();
    }
}
