namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Features.RenderPipeline.Domain;

internal sealed record PlannedRenderEffectGraph(
    IReadOnlyList<RenderEffectKey> OrderedKeys,
    IReadOnlyDictionary<RenderEffectKey, RenderEffectPlan> Plans);

internal static class RenderEffectGraphPlanner
{
    private static readonly IComparer<RenderEffectKey> EffectKeyComparer =
        Comparer<RenderEffectKey>.Create((left, right) =>
        {
            int kind = StringComparer.Ordinal.Compare(left.Kind, right.Kind);
            return kind != 0 ? kind : StringComparer.Ordinal.Compare(left.Slot, right.Slot);
        });

    public static PlannedRenderEffectGraph Plan(
        IReadOnlyDictionary<RenderEffectKey, RenderEffectPlan> plans,
        IEnumerable<RenderSurfaceKey> rootSurfaces)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(rootSurfaces);
        var roots = rootSurfaces.ToHashSet();
        if (roots.Any(root => !root.IsValid))
            throw new ArgumentException("Root surface keys must be initialized.", nameof(rootSurfaces));
        var producers = new Dictionary<RenderSurfaceKey, RenderEffectKey>();
        var outgoing = plans.Keys.ToDictionary(
            key => key,
            _ => new HashSet<RenderEffectKey>());
        var indegree = plans.Keys.ToDictionary(key => key, _ => 0);

        foreach (var (key, plan) in plans)
        {
            if (plan.Key != key)
                throw new InvalidOperationException(
                    $"Factory planned effect '{plan.Key}' for requested key '{key}'.");
            foreach (var output in plan.Outputs)
            {
                if (!string.Equals(output.ProducerKind, key.Kind, StringComparison.Ordinal) ||
                    !string.Equals(output.ProducerSlot, key.Slot, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Effect '{key}' cannot publish foreign surface '{output}'.");
                if (roots.Contains(output))
                    throw new InvalidOperationException(
                        $"Effect '{key}' cannot replace root surface '{output}'.");
                if (!producers.TryAdd(output, key))
                    throw new InvalidOperationException(
                        $"Render surface '{output}' has multiple producers.");
            }
        }

        foreach (var (consumer, plan) in plans)
        {
            foreach (var input in plan.Inputs)
            {
                if (roots.Contains(input)) continue;
                if (!producers.TryGetValue(input, out var producer))
                    throw new InvalidOperationException(
                        $"Effect '{consumer}' requires missing render surface '{input}'.");
                if (outgoing[producer].Add(consumer)) indegree[consumer]++;
            }
        }

        var ready = new SortedSet<RenderEffectKey>(EffectKeyComparer);
        foreach (var (key, count) in indegree)
            if (count == 0) ready.Add(key);

        var ordered = new List<RenderEffectKey>(plans.Count);
        while (ready.Count > 0)
        {
            var key = ready.Min;
            ready.Remove(key);
            ordered.Add(key);
            foreach (var consumer in outgoing[key].OrderBy(value => value, EffectKeyComparer))
            {
                if (--indegree[consumer] == 0) ready.Add(consumer);
            }
        }

        if (ordered.Count != plans.Count)
            throw new InvalidOperationException("Render effect dependency graph contains a cycle.");
        return new PlannedRenderEffectGraph(ordered, plans);
    }
}
