namespace TransformHierarchy.Tests;

using System.Numerics;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.TransformHierarchy.Domain;
using GameEngine.Features.TransformHierarchy.Gameplay;

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
        VerifyWorldTransformAuthoring();
        VerifySceneBindingsAndAttachments();
        VerifyTypedTransformPrefabs();

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

    private static void VerifyWorldTransformAuthoring()
    {
        var hierarchy = new TransformHierarchy();
        TransformNodeHandle parent = hierarchy.Create(new LocalTransform2D(
            new Vector2(20, 30), 0f, Vector2.One));
        TransformNodeHandle child = hierarchy.CreateChild(parent, LocalTransform2D.Identity);

        hierarchy.SetWorldTransform(child, new LocalTransform2D(
            new Vector2(45, 60), 0f, new Vector2(2, 3)));
        CheckVector(hierarchy.GetLocalTransform(child).Position, new Vector2(25, 30),
            "SetWorldTransform converts through the parent");
        CheckVector(hierarchy.GetWorldTransform(child).Position, new Vector2(45, 60),
            "GetWorldTransform exposes decomposed world TRS");
    }

    private static void VerifySceneBindingsAndAttachments()
    {
        using var transforms = new SceneTransformRuntime();
        var scene = new SceneAggregate("transform-authoring");
        var parent = new BoundProbe(transforms, new Vector2D(100, 100));
        var child = new BoundProbe(
            transforms,
            new Vector2D(10, 0),
            parent.Binding.Anchor);
        TransformAnchor muzzle = parent.Binding.CreateAttachment(
            "muzzle",
            new LocalTransform2D(new Vector2(0, -20), 0f, Vector2.One));

        scene.Add(parent);
        scene.Add(child);
        transforms.Synchronize();

        Check(child.Position == new Vector2D(110, 100),
            "Bound child publishes composed world position to GameInstance");
        CheckVector(muzzle.WorldPosition, new Vector2(100, 80),
            "Pure attachment composes without a GameInstance");

        parent.Position = new Vector2D(150, 120);
        CheckVector(muzzle.WorldPosition, new Vector2(150, 100),
            "Attachment reads observe same-Step direct owner motion");
        transforms.Synchronize();
        Check(child.Position == new Vector2D(160, 120),
            "Legacy direct world edits remain compatible for bound instances");
        CheckVector(muzzle.WorldPosition, new Vector2(150, 100),
            "Attachment remains stable after synchronization boundary");

        TransformAnchor lateAttachment = parent.Binding.CreateAttachment(
            "late",
            new LocalTransform2D(new Vector2(5, 0), 0f, Vector2.One));
        Check(!lateAttachment.IsReady,
            "Attachments created after Scene entry wait for the safe synchronization boundary");
        transforms.Synchronize();
        CheckVector(lateAttachment.WorldPosition, new Vector2(155, 120),
            "Deferred attachment becomes usable at the next synchronization boundary");

        Vector2D preserved = child.Position;
        scene.Destroy(parent.Id);
        transforms.Synchronize();
        Check(child.Position == preserved && scene.FindById(child.Id) is not null,
            "Destroying a spatial parent preserves and detaches surviving children");
        Check(muzzle.IsDestroyed,
            "Owner-created pure attachments follow owner Scene lifecycle");
        Check(lateAttachment.IsDestroyed,
            "Deferred attachments share the same owner lifecycle");

        var cycleA = new BoundProbe(transforms, Vector2D.Zero);
        var cycleB = new BoundProbe(transforms, Vector2D.Zero);
        scene.Add(cycleA);
        scene.Add(cycleB);
        cycleA.Binding.Anchor.AttachTo(cycleB.Binding.Anchor);
        CheckThrows<InvalidOperationException>(
            () => cycleB.Binding.Anchor.AttachTo(cycleA.Binding.Anchor),
            "Queued authoring cycles are rejected before the synchronization boundary");

        transforms.Synchronize();
        Vector2D cycleWorld = cycleA.Position;
        cycleA.Binding.Anchor.Detach(TransformReparentMode.KeepWorld);
        Check(cycleA.Position == cycleWorld,
            "Queued detach does not mutate published world pose during Step");
        transforms.Synchronize();
        Check(cycleA.Position == cycleWorld,
            "KeepWorld detach preserves the published world pose at commit");
    }

    private static void VerifyTypedTransformPrefabs()
    {
        var prefab = new TransformPrefab<TestRig>(
            "test.ship-rig",
            static builder =>
            {
                TransformNodeRef<TestWeapon> weapon = builder.Attachment<TestWeapon>(
                    "weapon",
                    new LocalTransform2D(new Vector2(10, 0), 0f, Vector2.One));
                TransformNodeRef<TestMuzzle> muzzle = builder.Attachment<TestMuzzle, TestWeapon>(
                    "muzzle",
                    new LocalTransform2D(new Vector2(0, -5), 0f, Vector2.One),
                    weapon);
                return new TestRig(weapon, muzzle);
            });

        using var transforms = new SceneTransformRuntime();
        var scene = new SceneAggregate("typed-transform-prefab");
        var root = new PrefabProbe(new Vector2D(100, 100));
        TransformPrefabInstance<TestRig> instance = prefab.Instantiate(root, transforms);
        var child = new BoundProbe(transforms, new Vector2D(3, 0));
        instance.Parts.Weapon.Attach(child.Binding);
        scene.Add(root);
        scene.Add(child);
        transforms.Synchronize();

        Check(instance.Name == "test.ship-rig" &&
              instance.Parts.Weapon.Name == "weapon" &&
              instance.Parts.Muzzle.Name == "muzzle",
            "Transform Prefab returns stable typed named node references");
        CheckVector(instance.Parts.Muzzle.WorldPosition, new Vector2(110, 95),
            "Nested root to weapon to muzzle topology composes deterministically");
        Check(child.Position == new Vector2D(113, 100),
            "A typed Prefab node can parent an independent GameInstance binding");

        scene.Destroy(root.Id);
        transforms.Synchronize();
        Check(!instance.Parts.Weapon.IsReady && !instance.Parts.Muzzle.IsReady,
            "Typed pure nodes follow their Prefab root Scene lifecycle");
        Check(child.Position == new Vector2D(113, 100),
            "Attached GameInstance survives Prefab root destruction with world pose preserved");

        using var failedRuntime = new SceneTransformRuntime();
        var invalid = new TransformPrefab<int>(
            "invalid.duplicates",
            static builder =>
            {
                _ = builder.Attachment<TestWeapon>("same", LocalTransform2D.Identity);
                _ = builder.Attachment<TestMuzzle>("same", LocalTransform2D.Identity);
                return 0;
            });
        CheckThrows<ArgumentException>(
            () => invalid.Instantiate(new PrefabProbe(Vector2D.Zero), failedRuntime),
            "Duplicate attachment names are rejected during Prefab assembly");
        Check(failedRuntime.DeclaredAnchorCount == 0 && failedRuntime.ActiveNodeCount == 0,
            "Failed Prefab assembly rolls back every declared transform node");

        TransformPrefabBuilder? escapedBuilder = null;
        var escaping = new TransformPrefab<int>(
            "invalid.escaped-builder",
            builder =>
            {
                escapedBuilder = builder;
                return 1;
            });
        _ = escaping.Instantiate(new PrefabProbe(Vector2D.Zero), failedRuntime);
        CheckThrows<InvalidOperationException>(
            () => escapedBuilder!.Attachment<TestWeapon>("late", LocalTransform2D.Identity),
            "Transform Prefab builder freezes after its assembly callback");

        var duplicateOwner = new PrefabProbe(Vector2D.Zero);
        _ = duplicateOwner.UseTransformHierarchy(failedRuntime);
        CheckThrows<InvalidOperationException>(
            () => duplicateOwner.UseTransformHierarchy(failedRuntime),
            "One GameInstance cannot publish two transform hierarchy bindings");
    }

    private sealed class BoundProbe : GameInstance
    {
        public BoundProbe(
            SceneTransformRuntime transforms,
            Vector2D position,
            TransformAnchor? parent = null)
        {
            Position = position;
            Binding = this.UseTransformHierarchy(transforms);
            if (parent is not null)
            {
                Binding.LocalTransform = new LocalTransform2D(
                    new Vector2((float)position.X, (float)position.Y),
                    0f,
                    Vector2.One);
                Binding.Anchor.AttachTo(parent);
            }
        }

        public TransformBindingBehavior Binding { get; }
    }

    private sealed class PrefabProbe : GameInstance
    {
        public PrefabProbe(Vector2D position) => Position = position;
    }

    private sealed class TestWeapon { }
    private sealed class TestMuzzle { }
    private readonly record struct TestRig(
        TransformNodeRef<TestWeapon> Weapon,
        TransformNodeRef<TestMuzzle> Muzzle);

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
