namespace TransformHierarchy.Tests;

using System.Numerics;
using GameEngine.Features.TransformHierarchy.Domain;

internal static class Program
{
    private const float Epsilon = 0.001f;
    private static int s_failures;

    private static void Main()
    {
        Console.WriteLine("=== Transform Hierarchy Smoke Tests ===\n");

        VerifyIdentityAndScreenSpaceRotation();
        VerifyDeepCompositionAndNegativeScale();
        VerifyStableGenerationHandles();
        VerifyReparentModes();
        VerifyInvalidOperations();
        VerifyDestroySubtreeAndRevisions();
        VerifyAllocationFreeSteadyState();

        Console.WriteLine();
        Console.WriteLine(s_failures == 0
            ? "=== All Transform Hierarchy smoke tests passed ==="
            : $"=== {s_failures} Transform Hierarchy test(s) FAILED ===");
        Environment.ExitCode = s_failures == 0 ? 0 : 1;
    }

    private static void VerifyIdentityAndScreenSpaceRotation()
    {
        var hierarchy = new TransformHierarchy();
        TransformNodeHandle root = hierarchy.Create(new LocalTransform2D(
            new Vector2(10, 20),
            MathF.PI / 2f,
            new Vector2(2, 3)));

        CheckVector(hierarchy.GetWorldPosition(root), new Vector2(10, 20),
            "Root world position equals its local position");
        CheckVector(hierarchy.TransformPointToWorld(root, Vector2.UnitX), new Vector2(10, 18),
            "Positive radians rotate counter-clockwise on a Y-down screen");
        Check(!hierarchy.HasPendingWorldChanges && hierarchy.PendingDirtyNodeCount == 0,
            "World reads settle all pending transform changes");
    }

    private static void VerifyDeepCompositionAndNegativeScale()
    {
        var hierarchy = new TransformHierarchy(1);
        TransformNodeHandle rotated = hierarchy.Create(new LocalTransform2D(
            new Vector2(100, 50),
            MathF.PI / 2f,
            Vector2.One));
        TransformNodeHandle child = hierarchy.CreateChild(rotated, new LocalTransform2D(
            new Vector2(10, 0),
            0f,
            Vector2.One));
        CheckVector(hierarchy.GetWorldPosition(child), new Vector2(100, 40),
            "Child translation composes through parent rotation");

        TransformNodeHandle mirrored = hierarchy.Create(new LocalTransform2D(
            Vector2.Zero,
            0f,
            new Vector2(-2, 3)));
        CheckVector(hierarchy.TransformPointToWorld(mirrored, new Vector2(4, 2)), new Vector2(-8, 6),
            "Negative non-uniform scale is preserved");

        var deep = new TransformHierarchy(2);
        TransformNodeHandle current = deep.Create(new LocalTransform2D(
            Vector2.One,
            0f,
            Vector2.One));
        const int depth = 2048;
        for (int i = 1; i < depth; i++)
        {
            current = deep.CreateChild(current, new LocalTransform2D(
                Vector2.One,
                0f,
                Vector2.One));
        }

        deep.UpdateWorldTransforms();
        CheckVector(deep.GetWorldPosition(current), new Vector2(depth, depth),
            "Deep hierarchies update iteratively without recursion");
    }

    private static void VerifyStableGenerationHandles()
    {
        var hierarchy = new TransformHierarchy(1);
        TransformNodeHandle first = hierarchy.Create();
        int reusedIndex = first.Index;
        ulong firstGeneration = first.Generation;
        hierarchy.Destroy(first);
        TransformNodeHandle second = hierarchy.Create();

        Check(second.Index == reusedIndex && second.Generation != firstGeneration,
            "Reused slots receive a new generation");
        Check(!hierarchy.IsAlive(first) && hierarchy.IsAlive(second),
            "Stale handles cannot alias a reused node");
        CheckThrows<ArgumentException>(() => hierarchy.GetWorldMatrix(first),
            "Stale handle access is rejected explicitly");
    }

    private static void VerifyReparentModes()
    {
        var hierarchy = new TransformHierarchy();
        TransformNodeHandle firstParent = hierarchy.Create(new LocalTransform2D(
            new Vector2(100, 20),
            0.25f,
            new Vector2(2, 2)));
        TransformNodeHandle secondParent = hierarchy.Create(new LocalTransform2D(
            new Vector2(-20, 80),
            -0.4f,
            new Vector2(-1.5f, 1.5f)));
        TransformNodeHandle child = hierarchy.CreateChild(firstParent, new LocalTransform2D(
            new Vector2(12, -3),
            0.6f,
            new Vector2(-1, 2)));

        Matrix3x2 before = hierarchy.GetWorldMatrix(child);
        hierarchy.SetParent(child, secondParent, TransformReparentMode.KeepWorld);
        CheckMatrix(hierarchy.GetWorldMatrix(child), before,
            "KeepWorld reparenting preserves the complete world matrix");
        Check(hierarchy.GetParent(child) == secondParent &&
              hierarchy.GetChildCount(firstParent) == 0 &&
              hierarchy.GetChildCount(secondParent) == 1,
            "Reparenting maintains parent and child links");

        LocalTransform2D localBeforeDetach = hierarchy.GetLocalTransform(child);
        hierarchy.Detach(child, TransformReparentMode.KeepLocal);
        Check(hierarchy.GetParent(child).IsEmpty &&
              hierarchy.GetLocalTransform(child) == localBeforeDetach,
            "KeepLocal detach preserves local TRS");

        TransformNodeHandle keepWorldChild = hierarchy.CreateChild(firstParent, new LocalTransform2D(
            new Vector2(3, 4),
            -0.2f,
            new Vector2(0.5f, 0.5f)));
        Matrix3x2 detachedWorld = hierarchy.GetWorldMatrix(keepWorldChild);
        hierarchy.Detach(keepWorldChild, TransformReparentMode.KeepWorld);
        CheckMatrix(hierarchy.GetWorldMatrix(keepWorldChild), detachedWorld,
            "KeepWorld detach preserves the complete world matrix");
    }

    private static void VerifyInvalidOperations()
    {
        var hierarchy = new TransformHierarchy();
        TransformNodeHandle root = hierarchy.Create();
        TransformNodeHandle child = hierarchy.CreateChild(root, LocalTransform2D.Identity);
        TransformNodeHandle grandchild = hierarchy.CreateChild(child, LocalTransform2D.Identity);

        CheckThrows<InvalidOperationException>(
            () => hierarchy.SetParent(root, grandchild),
            "Cycles are rejected before mutating hierarchy links");
        Check(hierarchy.GetParent(root).IsEmpty && hierarchy.GetParent(child) == root,
            "Cycle failure leaves the hierarchy unchanged");

        var other = new TransformHierarchy();
        TransformNodeHandle foreign = other.Create();
        CheckThrows<ArgumentException>(() => hierarchy.SetParent(child, foreign),
            "Cross-hierarchy handles are rejected");
        CheckThrows<ArgumentException>(() => hierarchy.GetWorldMatrix(TransformNodeHandle.None),
            "Empty handles are rejected");
        CheckThrows<ArgumentException>(
            () => hierarchy.SetLocalTransform(root, new LocalTransform2D(
                new Vector2(float.NaN, 0),
                0,
                Vector2.One)),
            "Non-finite local transforms are rejected");

        TransformNodeHandle singularParent = hierarchy.Create(new LocalTransform2D(
            Vector2.Zero,
            0f,
            new Vector2(0, 1)));
        Matrix3x2 childBefore = hierarchy.GetWorldMatrix(child);
        CheckThrows<InvalidOperationException>(
            () => hierarchy.SetParent(child, singularParent, TransformReparentMode.KeepWorld),
            "KeepWorld explicitly rejects a non-invertible parent");
        CheckMatrix(hierarchy.GetWorldMatrix(child), childBefore,
            "Failed KeepWorld operation is atomic");

        TransformNodeHandle nonUniformParent = hierarchy.Create(new LocalTransform2D(
            Vector2.Zero,
            0.7f,
            new Vector2(2, 1)));
        TransformNodeHandle rotatedWorld = hierarchy.Create(new LocalTransform2D(
            new Vector2(20, 10),
            -0.3f,
            Vector2.One));
        CheckThrows<InvalidOperationException>(
            () => hierarchy.SetParent(
                rotatedWorld,
                nonUniformParent,
                TransformReparentMode.KeepWorld),
            "KeepWorld rejects affine shear that local TRS cannot represent");
        Check(hierarchy.GetParent(rotatedWorld).IsEmpty,
            "Shear rejection leaves the original parent unchanged");
    }

    private static void VerifyDestroySubtreeAndRevisions()
    {
        var hierarchy = new TransformHierarchy();
        TransformNodeHandle root = hierarchy.Create();
        TransformNodeHandle child = hierarchy.CreateChild(root, LocalTransform2D.Identity);
        TransformNodeHandle grandchild = hierarchy.CreateChild(child, LocalTransform2D.Identity);
        hierarchy.UpdateWorldTransforms();
        ulong settledWorldRevision = hierarchy.GetWorldRevision(grandchild);
        ulong revisionBefore = hierarchy.Revision;

        hierarchy.SetLocalTransform(root, new LocalTransform2D(
            new Vector2(2, 3),
            0.1f,
            Vector2.One));
        Check(hierarchy.Revision == revisionBefore + 1 &&
              hierarchy.PendingDirtyNodeCount == 3,
            "A local edit increments revision and dirties only its subtree");
        hierarchy.UpdateWorldTransforms();
        Check(hierarchy.GetWorldRevision(grandchild) > settledWorldRevision,
            "Descendant world revision advances after propagation");

        hierarchy.Destroy(root);
        Check(hierarchy.Count == 0 &&
              !hierarchy.IsAlive(root) &&
              !hierarchy.IsAlive(child) &&
              !hierarchy.IsAlive(grandchild),
            "Destroy removes the complete subtree and invalidates every handle");
    }

    private static void VerifyAllocationFreeSteadyState()
    {
        var hierarchy = new TransformHierarchy(128);
        TransformNodeHandle root = hierarchy.Create();
        TransformNodeHandle leaf = root;
        for (int i = 0; i < 100; i++)
        {
            leaf = hierarchy.CreateChild(leaf, new LocalTransform2D(
                new Vector2(1, 2),
                0.01f,
                Vector2.One));
        }

        hierarchy.UpdateWorldTransforms();
        for (int i = 0; i < 64; i++)
        {
            hierarchy.UpdateWorldTransforms();
            _ = hierarchy.GetWorldMatrix(leaf);
            _ = hierarchy.GetWorldPosition(leaf);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 4096; i++)
        {
            hierarchy.UpdateWorldTransforms();
            _ = hierarchy.GetWorldMatrix(leaf);
            _ = hierarchy.GetWorldPosition(leaf);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Check(allocated == 0,
            $"Settled update and world reads remain allocation-free ({allocated:N0} B)");

        hierarchy.SetLocalTransform(root, new LocalTransform2D(
            new Vector2(5, 6),
            0.2f,
            Vector2.One));
        before = GC.GetAllocatedBytesForCurrentThread();
        hierarchy.UpdateWorldTransforms();
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0,
            $"Dirty subtree propagation uses no temporary allocations ({allocated:N0} B)");
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        s_failures++;
        Console.WriteLine($"  [FAIL] {name}");
    }

    private static void CheckVector(Vector2 actual, Vector2 expected, string name) =>
        Check(Vector2.Distance(actual, expected) <= Epsilon,
            $"{name} (expected {expected}, actual {actual})");

    private static void CheckMatrix(Matrix3x2 actual, Matrix3x2 expected, string name) =>
        Check(MathF.Abs(actual.M11 - expected.M11) <= Epsilon &&
              MathF.Abs(actual.M12 - expected.M12) <= Epsilon &&
              MathF.Abs(actual.M21 - expected.M21) <= Epsilon &&
              MathF.Abs(actual.M22 - expected.M22) <= Epsilon &&
              MathF.Abs(actual.M31 - expected.M31) <= Epsilon &&
              MathF.Abs(actual.M32 - expected.M32) <= Epsilon,
            name);

    private static void CheckThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (TException)
        {
            Check(true, name);
        }
    }
}
