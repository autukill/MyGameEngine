namespace GameEngine.Features.TransformHierarchy.Domain;

using System.Numerics;
using System.Threading;

/// <summary>
/// Array-backed transform tree with stable generation handles and allocation-free steady-state
/// world propagation. This type owns only transform nodes; nodes do not require game instances,
/// render resources, or platform services.
/// </summary>
public sealed class TransformHierarchy
{
    private const int DefaultCapacity = 16;
    private const float DecompositionTolerance = 0.0001f;
    private static long s_nextHierarchyId;

    private struct Slot
    {
        public bool Alive;
        public bool Dirty;
        public ulong Generation;
        public int Parent;
        public int FirstChild;
        public int NextSibling;
        public int PreviousSibling;
        public int ChildCount;
        public int NextFree;
        public LocalTransform2D Local;
        public Matrix3x2 World;
        public ulong WorldRevision;
    }

    private readonly long _hierarchyId;
    private Slot[] _slots;
    private int _nextUnused;
    private int _freeHead = -1;
    private int _count;
    private int _dirtyCount;
    private ulong _revision;

    public TransformHierarchy(int initialCapacity = DefaultCapacity)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _hierarchyId = NextHierarchyId();
        _slots = new Slot[Math.Max(initialCapacity, 1)];
    }

    public int Count => _count;
    public int Capacity => _slots.Length;
    public int PendingDirtyNodeCount => _dirtyCount;
    public bool HasPendingWorldChanges => _dirtyCount != 0;

    /// <summary>Increases once for every successful structural or local-transform mutation.</summary>
    public ulong Revision => _revision;

    public TransformNodeHandle Create() => Create(LocalTransform2D.Identity);

    public TransformNodeHandle Create(in LocalTransform2D localTransform)
    {
        LocalTransform2D.Validate(localTransform, nameof(localTransform));
        int index = AllocateSlot(localTransform);
        AdvanceRevision();
        return HandleFor(index);
    }

    public TransformNodeHandle CreateChild(
        TransformNodeHandle parent,
        in LocalTransform2D localTransform)
    {
        int parentIndex = RequireAlive(parent, nameof(parent));
        LocalTransform2D.Validate(localTransform, nameof(localTransform));

        int index = AllocateSlot(localTransform);
        LinkAsFirstChild(index, parentIndex);
        AdvanceRevision();
        return HandleFor(index);
    }

    /// <summary>Destroys the node and its complete subtree without recursion.</summary>
    public void Destroy(TransformNodeHandle node)
    {
        int rootIndex = RequireAlive(node, nameof(node));
        int current = rootIndex;

        while (true)
        {
            ref Slot currentSlot = ref _slots[current];
            if (currentSlot.FirstChild >= 0)
            {
                current = currentSlot.FirstChild;
                continue;
            }

            int parent = currentSlot.Parent;
            int nextSibling = currentSlot.NextSibling;
            ReleaseSlot(current);

            if (current == rootIndex)
                break;

            current = nextSibling >= 0 ? nextSibling : parent;
        }

        AdvanceRevision();
    }

    public bool IsAlive(TransformNodeHandle node)
    {
        if (node.HierarchyId != _hierarchyId ||
            node.Index < 0 ||
            node.Index >= _nextUnused)
        {
            return false;
        }

        ref readonly Slot slot = ref _slots[node.Index];
        return slot.Alive && slot.Generation == node.Generation;
    }

    public LocalTransform2D GetLocalTransform(TransformNodeHandle node) =>
        _slots[RequireAlive(node, nameof(node))].Local;

    public void SetLocalTransform(
        TransformNodeHandle node,
        in LocalTransform2D localTransform)
    {
        int index = RequireAlive(node, nameof(node));
        LocalTransform2D.Validate(localTransform, nameof(localTransform));

        if (_slots[index].Local == localTransform)
            return;

        _slots[index].Local = localTransform;
        MarkSubtreeDirty(index);
        AdvanceRevision();
    }

    public TransformNodeHandle GetParent(TransformNodeHandle node)
    {
        int parent = _slots[RequireAlive(node, nameof(node))].Parent;
        return parent < 0 ? TransformNodeHandle.None : HandleFor(parent);
    }

    public int GetChildCount(TransformNodeHandle node) =>
        _slots[RequireAlive(node, nameof(node))].ChildCount;

    public ulong GetWorldRevision(TransformNodeHandle node)
    {
        int index = RequireAlive(node, nameof(node));
        UpdateWorldTransforms();
        return _slots[index].WorldRevision;
    }

    public Matrix3x2 GetWorldMatrix(TransformNodeHandle node)
    {
        int index = RequireAlive(node, nameof(node));
        UpdateWorldTransforms();
        return _slots[index].World;
    }

    public Vector2 GetWorldPosition(TransformNodeHandle node)
    {
        Matrix3x2 world = GetWorldMatrix(node);
        return new Vector2(world.M31, world.M32);
    }

    public Vector2 TransformPointToWorld(TransformNodeHandle node, Vector2 localPoint) =>
        Vector2.Transform(localPoint, GetWorldMatrix(node));

    public void SetParent(
        TransformNodeHandle node,
        TransformNodeHandle parent,
        TransformReparentMode mode = TransformReparentMode.KeepLocal)
    {
        int nodeIndex = RequireAlive(node, nameof(node));
        int parentIndex = RequireAlive(parent, nameof(parent));

        if (nodeIndex == parentIndex || IsDescendant(parentIndex, nodeIndex))
            throw new InvalidOperationException("A transform hierarchy cannot contain a cycle.");

        if (_slots[nodeIndex].Parent == parentIndex)
            return;

        LocalTransform2D nextLocal = _slots[nodeIndex].Local;
        if (mode == TransformReparentMode.KeepWorld)
        {
            UpdateWorldTransforms();
            Matrix3x2 oldWorld = _slots[nodeIndex].World;
            Matrix3x2 parentWorld = _slots[parentIndex].World;
            if (!Matrix3x2.Invert(parentWorld, out Matrix3x2 inverseParent))
            {
                throw new InvalidOperationException(
                    "KeepWorld reparenting requires an invertible parent world matrix.");
            }

            nextLocal = DecomposeTrsOrThrow(oldWorld * inverseParent);
        }
        else if (mode != TransformReparentMode.KeepLocal)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        UnlinkFromParent(nodeIndex);
        LinkAsFirstChild(nodeIndex, parentIndex);
        _slots[nodeIndex].Local = nextLocal;
        MarkSubtreeDirty(nodeIndex);
        AdvanceRevision();
    }

    public void Detach(
        TransformNodeHandle node,
        TransformReparentMode mode = TransformReparentMode.KeepLocal)
    {
        int nodeIndex = RequireAlive(node, nameof(node));
        if (_slots[nodeIndex].Parent < 0)
            return;

        LocalTransform2D nextLocal = _slots[nodeIndex].Local;
        if (mode == TransformReparentMode.KeepWorld)
        {
            UpdateWorldTransforms();
            nextLocal = DecomposeTrsOrThrow(_slots[nodeIndex].World);
        }
        else if (mode != TransformReparentMode.KeepLocal)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        UnlinkFromParent(nodeIndex);
        _slots[nodeIndex].Local = nextLocal;
        MarkSubtreeDirty(nodeIndex);
        AdvanceRevision();
    }

    /// <summary>
    /// Recomputes every dirty world matrix in parent-before-child order. Repeated calls without
    /// mutations perform no writes and allocate no managed memory.
    /// </summary>
    public void UpdateWorldTransforms()
    {
        if (_dirtyCount == 0)
            return;

        for (int root = 0; root < _nextUnused; root++)
        {
            if (!_slots[root].Alive || _slots[root].Parent >= 0)
                continue;

            UpdateSubtree(root);
        }
    }

    private int AllocateSlot(in LocalTransform2D localTransform)
    {
        int index;
        if (_freeHead >= 0)
        {
            index = _freeHead;
            _freeHead = _slots[index].NextFree;
        }
        else
        {
            if (_nextUnused == _slots.Length)
                Array.Resize(ref _slots, checked(_slots.Length * 2));
            index = _nextUnused++;
        }

        ref Slot slot = ref _slots[index];
        if (slot.Generation == 0)
            slot.Generation = 1;
        slot.Alive = true;
        slot.Dirty = true;
        slot.Parent = -1;
        slot.FirstChild = -1;
        slot.NextSibling = -1;
        slot.PreviousSibling = -1;
        slot.ChildCount = 0;
        slot.NextFree = -1;
        slot.Local = localTransform;
        slot.World = Matrix3x2.Identity;
        slot.WorldRevision = 0;

        _count++;
        _dirtyCount++;
        return index;
    }

    private void ReleaseSlot(int index)
    {
        UnlinkFromParent(index);
        ref Slot slot = ref _slots[index];
        if (slot.Dirty)
            _dirtyCount--;

        slot.Alive = false;
        slot.Dirty = false;
        slot.FirstChild = -1;
        slot.ChildCount = 0;
        slot.Local = default;
        slot.World = default;
        slot.WorldRevision = 0;
        slot.Generation = slot.Generation == ulong.MaxValue ? 1 : slot.Generation + 1;
        slot.NextFree = _freeHead;
        _freeHead = index;
        _count--;
    }

    private void LinkAsFirstChild(int child, int parent)
    {
        ref Slot childSlot = ref _slots[child];
        ref Slot parentSlot = ref _slots[parent];
        int previousFirst = parentSlot.FirstChild;

        childSlot.Parent = parent;
        childSlot.PreviousSibling = -1;
        childSlot.NextSibling = previousFirst;
        if (previousFirst >= 0)
            _slots[previousFirst].PreviousSibling = child;
        parentSlot.FirstChild = child;
        parentSlot.ChildCount++;
    }

    private void UnlinkFromParent(int child)
    {
        ref Slot childSlot = ref _slots[child];
        int parent = childSlot.Parent;
        if (parent < 0)
            return;

        if (childSlot.PreviousSibling >= 0)
            _slots[childSlot.PreviousSibling].NextSibling = childSlot.NextSibling;
        else
            _slots[parent].FirstChild = childSlot.NextSibling;

        if (childSlot.NextSibling >= 0)
            _slots[childSlot.NextSibling].PreviousSibling = childSlot.PreviousSibling;

        _slots[parent].ChildCount--;
        childSlot.Parent = -1;
        childSlot.PreviousSibling = -1;
        childSlot.NextSibling = -1;
    }

    private void MarkSubtreeDirty(int root)
    {
        int current = root;
        while (true)
        {
            ref Slot slot = ref _slots[current];
            if (!slot.Dirty)
            {
                slot.Dirty = true;
                _dirtyCount++;
            }

            if (slot.FirstChild >= 0)
            {
                current = slot.FirstChild;
                continue;
            }

            while (current != root && _slots[current].NextSibling < 0)
                current = _slots[current].Parent;

            if (current == root)
                break;

            current = _slots[current].NextSibling;
        }
    }

    private void UpdateSubtree(int root)
    {
        int current = root;
        while (true)
        {
            ref Slot slot = ref _slots[current];
            if (slot.Dirty)
            {
                Matrix3x2 local = slot.Local.ToMatrix();
                slot.World = slot.Parent < 0 ? local : local * _slots[slot.Parent].World;
                slot.Dirty = false;
                slot.WorldRevision = _revision;
                _dirtyCount--;
            }

            if (slot.FirstChild >= 0)
            {
                current = slot.FirstChild;
                continue;
            }

            while (current != root && _slots[current].NextSibling < 0)
                current = _slots[current].Parent;

            if (current == root)
                break;

            current = _slots[current].NextSibling;
        }
    }

    private bool IsDescendant(int candidate, int ancestor)
    {
        for (int current = candidate; current >= 0; current = _slots[current].Parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    private int RequireAlive(TransformNodeHandle node, string parameterName)
    {
        if (node.HierarchyId != _hierarchyId)
        {
            throw new ArgumentException(
                node.IsEmpty
                    ? "Transform node handle is empty."
                    : "Transform node belongs to a different hierarchy.",
                parameterName);
        }

        if (node.Index < 0 || node.Index >= _nextUnused)
            throw new ArgumentException("Transform node handle is invalid.", parameterName);

        ref readonly Slot slot = ref _slots[node.Index];
        if (!slot.Alive || slot.Generation != node.Generation)
            throw new ArgumentException("Transform node handle is stale or invalid.", parameterName);

        return node.Index;
    }

    private TransformNodeHandle HandleFor(int index) =>
        new(index, _slots[index].Generation, _hierarchyId);

    private void AdvanceRevision()
    {
        _revision = _revision == ulong.MaxValue ? 1 : _revision + 1;
    }

    private static long NextHierarchyId()
    {
        long id = Interlocked.Increment(ref s_nextHierarchyId);
        if (id == 0)
            id = Interlocked.Increment(ref s_nextHierarchyId);
        return id;
    }

    private static LocalTransform2D DecomposeTrsOrThrow(in Matrix3x2 matrix)
    {
        if (!IsFinite(matrix))
            throw new InvalidOperationException("World transform contains non-finite values.");

        float scaleX = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        float scaleY;
        float rotation;

        if (scaleX > float.Epsilon)
        {
            rotation = MathF.Atan2(-matrix.M12, matrix.M11);
            scaleY = matrix.GetDeterminant() / scaleX;
        }
        else
        {
            scaleY = MathF.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
            rotation = scaleY > float.Epsilon
                ? MathF.Atan2(matrix.M21, matrix.M22)
                : 0f;
        }

        var result = new LocalTransform2D(
            new Vector2(matrix.M31, matrix.M32),
            rotation,
            new Vector2(scaleX, scaleY));
        Matrix3x2 reconstructed = result.ToMatrix();
        if (!NearlyEqual(matrix, reconstructed))
        {
            throw new InvalidOperationException(
                "KeepWorld reparenting would require shear, which LocalTransform2D cannot represent.");
        }

        return result;
    }

    private static bool NearlyEqual(in Matrix3x2 left, in Matrix3x2 right)
    {
        float largest = MathF.Max(1f, MathF.Max(MaxAbs(left), MaxAbs(right)));
        float tolerance = largest * DecompositionTolerance;
        return MathF.Abs(left.M11 - right.M11) <= tolerance &&
               MathF.Abs(left.M12 - right.M12) <= tolerance &&
               MathF.Abs(left.M21 - right.M21) <= tolerance &&
               MathF.Abs(left.M22 - right.M22) <= tolerance &&
               MathF.Abs(left.M31 - right.M31) <= tolerance &&
               MathF.Abs(left.M32 - right.M32) <= tolerance;
    }

    private static float MaxAbs(in Matrix3x2 value) => MathF.Max(
        MathF.Max(MathF.Abs(value.M11), MathF.Abs(value.M12)),
        MathF.Max(
            MathF.Max(MathF.Abs(value.M21), MathF.Abs(value.M22)),
            MathF.Max(MathF.Abs(value.M31), MathF.Abs(value.M32))));

    private static bool IsFinite(in Matrix3x2 value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32);
}
