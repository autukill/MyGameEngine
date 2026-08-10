namespace GameEngine.Features.TransformHierarchy.Gameplay;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TransformHierarchy.Domain;

/// <summary>
/// Scene-scoped bridge between the allocation-free transform tree and GameInstance world
/// transforms. Spatial parenting never owns or destroys GameInstance lifecycle.
/// </summary>
public sealed class SceneTransformRuntime : IDisposable
{
    private readonly TransformHierarchy _hierarchy;
    private readonly List<TransformAnchor> _anchors = [];
    private long _nextAnchorId;
    private bool _disposed;

    public SceneTransformRuntime(int initialCapacity = 16) =>
        _hierarchy = new TransformHierarchy(initialCapacity);

    public int ActiveNodeCount => _hierarchy.Count;
    public int DeclaredAnchorCount => _anchors.Count;

    internal TransformAnchor CreateBindingAnchor(
        TransformBindingBehavior binding,
        in LocalTransform2D initial) =>
        AddAnchor(binding, binding, initial, null);

    internal TransformAnchor CreateAttachment(
        TransformBindingBehavior lifetimeOwner,
        TransformAnchor parent,
        string name,
        in LocalTransform2D local)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RequireOwned(parent, nameof(parent));
        if (!ReferenceEquals(parent.LifetimeOwner, lifetimeOwner))
        {
            throw new ArgumentException(
                "A pure attachment must share its declaring binding's lifetime owner.",
                nameof(parent));
        }
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor existing = _anchors[i];
            if (existing.Binding is null &&
                ReferenceEquals(existing.LifetimeOwner, lifetimeOwner) &&
                string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Transform attachment name '{name}' is already declared for this owner.",
                    nameof(name));
            }
        }
        var anchor = AddAnchor(null, lifetimeOwner, local, name);
        anchor.QueueParent(parent, TransformReparentMode.KeepLocal);
        return anchor;
    }

    internal void Register(TransformBindingBehavior binding)
    {
        ThrowIfDisposed();
        TransformAnchor anchor = binding.Anchor;
        if (anchor.IsReady) throw new InvalidOperationException("Transform binding is already active.");
        LocalTransform2D initial = anchor.HasExplicitLocalEdit
            ? anchor.CachedLocal
            : ToLocal(binding.Owner.Transform);
        anchor.Activate(_hierarchy.Create(initial));
        binding.SetLastPublished(binding.Owner.Transform);
        ActivateReadyAnchors();
    }

    internal void Unregister(TransformBindingBehavior binding)
    {
        if (_disposed) return;
        for (int i = _anchors.Count - 1; i >= 0; i--)
        {
            TransformAnchor candidate = _anchors[i];
            if (ReferenceEquals(candidate.LifetimeOwner, binding) &&
                !ReferenceEquals(candidate, binding.Anchor))
            {
                DestroyAnchor(candidate);
            }
        }
        DestroyAnchor(binding.Anchor);
    }

    internal void DiscardAuthoring(TransformBindingBehavior binding) => Unregister(binding);

    /// <summary>
    /// Applies queued parent changes, imports compatible direct world edits, propagates world
    /// matrices, then publishes world TRS values back to bound GameInstances.
    /// </summary>
    public void Synchronize()
    {
        ThrowIfDisposed();
        ActivateReadyAnchors();

        ImportOwnerWorldEdits();

        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor anchor = _anchors[i];
            if (!anchor.IsReady || !anchor.HasPendingParent) continue;
            TransformAnchor? parent = anchor.PendingParent;
            if (parent is null)
                _hierarchy.Detach(anchor.Node, anchor.PendingMode);
            else
            {
                if (!parent.IsReady) continue;
                _hierarchy.SetParent(anchor.Node, parent.Node, anchor.PendingMode);
            }
            anchor.ClearPendingParent();
        }

        _hierarchy.UpdateWorldTransforms();
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor anchor = _anchors[i];
            if (!anchor.IsReady) continue;
            anchor.RefreshLocal(_hierarchy.GetLocalTransform(anchor.Node));
            if (anchor.Binding is { } binding)
            {
                Transform2D world = ToCore(_hierarchy.GetWorldTransform(anchor.Node));
                binding.Publish(world);
            }
            anchor.ClearExplicitLocalEdit();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = 0; i < _anchors.Count; i++)
            _anchors[i].Deactivate();
        _anchors.Clear();
    }

    internal LocalTransform2D GetLocal(TransformAnchor anchor)
    {
        RequireOwned(anchor, nameof(anchor));
        return anchor.IsReady ? _hierarchy.GetLocalTransform(anchor.Node) : anchor.CachedLocal;
    }

    internal void SetLocal(TransformAnchor anchor, in LocalTransform2D value)
    {
        ThrowIfDisposed();
        RequireOwned(anchor, nameof(anchor));
        LocalTransform2D.Validate(value, nameof(value));
        anchor.CacheLocal(value);
        if (anchor.IsReady) _hierarchy.SetLocalTransform(anchor.Node, value);
    }

    internal Matrix3x2 GetWorldMatrix(TransformAnchor anchor)
    {
        RequireReady(anchor);
        ImportOwnerWorldEdits();
        return _hierarchy.GetWorldMatrix(anchor.Node);
    }

    internal Vector2 GetWorldPosition(TransformAnchor anchor)
    {
        RequireReady(anchor);
        ImportOwnerWorldEdits();
        return _hierarchy.GetWorldPosition(anchor.Node);
    }

    internal Vector2 TransformPointToWorld(TransformAnchor anchor, Vector2 point)
    {
        RequireReady(anchor);
        ImportOwnerWorldEdits();
        return _hierarchy.TransformPointToWorld(anchor.Node, point);
    }

    internal void QueueParent(
        TransformAnchor anchor,
        TransformAnchor? parent,
        TransformReparentMode mode)
    {
        ThrowIfDisposed();
        RequireOwned(anchor, nameof(anchor));
        if (parent is not null) RequireOwned(parent, nameof(parent));
        if (ReferenceEquals(anchor, parent))
            throw new InvalidOperationException("A transform anchor cannot parent itself.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        for (TransformAnchor? current = parent;
             current is not null;
             current = FindLogicalParent(current))
        {
            if (ReferenceEquals(current, anchor))
                throw new InvalidOperationException("A transform hierarchy cannot contain a cycle.");
        }
        anchor.QueueParent(parent, mode);
    }

    internal void DestroyAttachment(TransformAnchor anchor)
    {
        ThrowIfDisposed();
        RequireOwned(anchor, nameof(anchor));
        if (anchor.Binding is not null)
            throw new InvalidOperationException("A GameInstance binding is destroyed by Scene lifecycle.");
        DestroyAnchor(anchor);
    }

    internal void WriteGameplayState(
        ref GameplayStateWriter writer,
        TransformBindingBehavior binding)
    {
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor anchor = _anchors[i];
            if (!ReferenceEquals(anchor.LifetimeOwner, binding)) continue;
            LocalTransform2D local = GetLocal(anchor);
            writer.Write("transform.anchor.id", anchor.Id);
            writer.Write("transform.anchor.isBinding", anchor.Binding is not null);
            writer.Write("transform.anchor.local.position",
                new Vector2D(local.Position.X, local.Position.Y));
            writer.Write("transform.anchor.local.rotation", local.RotationRadians);
            writer.Write("transform.anchor.local.scale.x", local.Scale.X);
            writer.Write("transform.anchor.local.scale.y", local.Scale.Y);
            writer.Write("transform.anchor.parent", FindParentId(anchor));
        }
    }

    private TransformAnchor AddAnchor(
        TransformBindingBehavior? binding,
        TransformBindingBehavior lifetimeOwner,
        in LocalTransform2D local,
        string? name)
    {
        LocalTransform2D.Validate(local, nameof(local));
        long id = checked(++_nextAnchorId);
        var result = new TransformAnchor(this, id, binding, lifetimeOwner, local, name);
        _anchors.Add(result);
        return result;
    }

    private void ActivateReadyAnchors()
    {
        bool progressed;
        do
        {
            progressed = false;
            for (int i = 0; i < _anchors.Count; i++)
            {
                TransformAnchor anchor = _anchors[i];
                if (anchor.IsReady || anchor.Binding is not null || !anchor.HasPendingParent)
                    continue;
                TransformAnchor? parent = anchor.PendingParent;
                if (parent is null || !parent.IsReady) continue;
                anchor.Activate(_hierarchy.CreateChild(parent.Node, anchor.CachedLocal));
                anchor.ClearPendingParent();
                progressed = true;
            }
        } while (progressed);
    }

    private void DestroyAnchor(TransformAnchor anchor)
    {
        if (anchor.IsDestroyed) return;
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor candidate = _anchors[i];
            if (ReferenceEquals(candidate.PendingParent, anchor))
                candidate.ClearPendingParent();
        }
        if (anchor.IsReady)
        {
            // Parenting is spatial only: preserve every surviving child's world pose.
            for (int i = 0; i < _anchors.Count; i++)
            {
                TransformAnchor child = _anchors[i];
                if (!child.IsReady || ReferenceEquals(child, anchor)) continue;
                if (_hierarchy.GetParent(child.Node) == anchor.Node)
                {
                    _hierarchy.Detach(child.Node, TransformReparentMode.KeepWorld);
                    child.ClearPendingParent();
                    child.RefreshLocal(_hierarchy.GetLocalTransform(child.Node));
                }
            }
            _hierarchy.Destroy(anchor.Node);
        }
        anchor.MarkDestroyed();
        _anchors.Remove(anchor);
    }

    private void ImportOwnerWorldEdits()
    {
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor anchor = _anchors[i];
            TransformBindingBehavior? binding = anchor.Binding;
            if (!anchor.IsReady || binding is null || anchor.HasExplicitLocalEdit) continue;
            Transform2D current = binding.Owner.Transform;
            if (current != binding.LastPublished)
                _hierarchy.SetWorldTransform(anchor.Node, ToLocal(current));
        }
    }

    private long FindParentId(TransformAnchor anchor)
    {
        if (!anchor.IsReady) return anchor.PendingParent?.Id ?? 0L;
        TransformNodeHandle parent = _hierarchy.GetParent(anchor.Node);
        if (parent.IsEmpty) return 0L;
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor candidate = _anchors[i];
            if (candidate.IsReady && candidate.Node == parent) return candidate.Id;
        }
        throw new InvalidOperationException("Transform parent is missing from the Scene runtime.");
    }

    private TransformAnchor? FindLogicalParent(TransformAnchor anchor)
    {
        if (anchor.HasPendingParent) return anchor.PendingParent;
        if (!anchor.IsReady) return null;
        TransformNodeHandle parent = _hierarchy.GetParent(anchor.Node);
        if (parent.IsEmpty) return null;
        for (int i = 0; i < _anchors.Count; i++)
        {
            TransformAnchor candidate = _anchors[i];
            if (candidate.IsReady && candidate.Node == parent) return candidate;
        }
        throw new InvalidOperationException("Transform parent is missing from the Scene runtime.");
    }

    private void RequireReady(TransformAnchor anchor)
    {
        RequireOwned(anchor, nameof(anchor));
        if (!anchor.IsReady)
            throw new InvalidOperationException(
                "Transform anchor is not active. Add its owner to a Scene and synchronize first.");
    }

    private void RequireOwned(TransformAnchor anchor, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(anchor, parameterName);
        if (!ReferenceEquals(anchor.Runtime, this))
            throw new ArgumentException("Transform anchor belongs to a different Scene runtime.", parameterName);
        if (anchor.IsDestroyed)
            throw new ObjectDisposedException(nameof(TransformAnchor));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static LocalTransform2D ToLocal(in Transform2D value) => new(
        new Vector2((float)value.Position.X, (float)value.Position.Y),
        value.Rotation,
        new Vector2((float)value.Scale.X, (float)value.Scale.Y));

    private static Transform2D ToCore(in LocalTransform2D value) => new()
    {
        Position = new Vector2D(value.Position.X, value.Position.Y),
        Rotation = value.RotationRadians,
        Scale = new Vector2D(value.Scale.X, value.Scale.Y)
    };
}
