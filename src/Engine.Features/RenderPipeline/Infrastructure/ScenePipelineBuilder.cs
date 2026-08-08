namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>在 Step/Draw 边界把领域效果状态原子映射为有逻辑 Surface 依赖的 RenderPass 图。</summary>
public sealed class ScenePipelineBuilder : IDisposable
{
    private readonly IRenderEffectGraphEditor _graph;
    private readonly IRenderTargetPool _targets;
    private readonly Dictionary<string, IRenderEffectFactory> _factories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<RenderSurfaceKey, RenderSurfaceRegistration> _rootSurfaces = new();
    private readonly Dictionary<RenderEffectKey, ActiveEffect> _active = new();
    private readonly List<RenderEffectKey> _activeOrder = new();
    private int _width;
    private int _height;
    private bool _disposed;

    public int ActiveEffectCount => _active.Count;

    public ScenePipelineBuilder(
        RenderPipeline pipeline,
        ViewportCompositorPass compositor,
        IRenderTargetPool targets,
        int width,
        int height)
        : this(new RenderEffectGraphEditor(pipeline, compositor), targets, width, height)
    {
    }

    public ScenePipelineBuilder(
        IRenderEffectGraphEditor graph,
        IRenderTargetPool targets,
        int width,
        int height)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _width = width;
        _height = height;
    }

    public void RegisterFactory(IRenderEffectFactory factory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(factory.Kind))
            throw new ArgumentException("Factory kind cannot be empty.", nameof(factory));
        if (!_factories.TryAdd(factory.Kind, factory))
            throw new ArgumentException(
                $"A render effect factory for '{factory.Kind}' is already registered.",
                nameof(factory));
    }

    /// <summary>注册由组合根拥有的借用 Surface；必须在首个效果创建前完成。</summary>
    public void RegisterRootSurface(
        RenderSurfaceKey key,
        RenderTarget2D surface,
        RenderSurfaceEncoding? encoding = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (!key.IsValid)
            throw new ArgumentException("Root render surface key must be initialized.", nameof(key));
        if (_active.Count != 0)
            throw new InvalidOperationException(
                "Root render surfaces cannot change while effects are active.");
        var spec = new RenderSurfaceSpec(
            key,
            surface.ColorFormat,
            encoding ?? (surface.ColorFormat == RenderTargetColorFormat.Rgba16Float
                ? RenderSurfaceEncoding.Linear
                : RenderSurfaceEncoding.Display));
        if (!_rootSurfaces.TryAdd(key, new RenderSurfaceRegistration(surface, spec)))
            throw new ArgumentException(
                $"Root render surface '{key}' is already registered.", nameof(key));
    }

    public int GetOwnerCount(RenderEffectKey key) =>
        _active.TryGetValue(key, out var effect) ? effect.Owners.Count : 0;

    public void ApplyEvents(IEnumerable<IDomainEvent> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(events);

        var candidate = CloneOwnerState();
        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case RenderEffectRequestedEvent requested:
                    ArgumentNullException.ThrowIfNull(requested.Descriptor);
                    if (!_factories.ContainsKey(requested.Descriptor.Key.Kind))
                        throw new InvalidOperationException(
                            $"No render effect factory is registered for " +
                            $"'{requested.Descriptor.Key.Kind}'.");
                    GetOrAdd(candidate, requested.Descriptor.Key)[requested.OwnerId] =
                        requested.Descriptor;
                    break;

                case RenderEffectReleasedEvent released:
                    RemoveOwner(candidate, released.OwnerId, released.EffectKey);
                    break;

                case InstanceDestroyedEvent destroyed:
                    RemoveOwnerFromAll(candidate, destroyed.Id);
                    break;

                case InstanceActivationChangedEvent { IsActive: false } activation:
                    RemoveOwnerFromAll(candidate, activation.Id);
                    break;
            }
        }

        foreach (var empty in candidate
                     .Where(pair => pair.Value.Count == 0)
                     .Select(pair => pair.Key)
                     .ToArray())
            candidate.Remove(empty);

        var planned = PlanCandidate(candidate);
        Reconcile(candidate, planned, _width, _height);
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (width == _width && height == _height) return;

        var owners = CloneOwnerState();
        var planned = PlanCandidate(owners);
        RebuildAll(owners, planned, width, height);
        _width = width;
        _height = height;
        _targets.TrimExceptSize(width, height);
    }

    private Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>>
        CloneOwnerState() =>
        _active.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<InstanceId, IRenderEffectDescriptor>(pair.Value.Owners));

    private PlannedRenderEffectGraph PlanCandidate(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> candidate)
    {
        var plans = new Dictionary<RenderEffectKey, RenderEffectPlan>();
        foreach (var (key, owners) in candidate)
        {
            if (!_factories.TryGetValue(key.Kind, out var factory))
                throw new InvalidOperationException(
                    $"No render effect factory is registered for '{key.Kind}'.");
            var plan = factory.Plan(key, owners) ??
                       throw new InvalidOperationException(
                           $"Effect factory '{key.Kind}' returned a null plan.");
            plans.Add(key, plan);
        }
        return RenderEffectGraphPlanner.Plan(
            plans,
            _rootSurfaces.Values.Select(root => root.Spec));
    }

    private void Reconcile(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> candidate,
        PlannedRenderEffectGraph planned,
        int width,
        int height)
    {
        bool structureChanged =
            _active.Count != candidate.Count ||
            planned.OrderedKeys.Count != _activeOrder.Count ||
            !planned.OrderedKeys.SequenceEqual(_activeOrder) ||
            planned.OrderedKeys.Any(key =>
                !_active.TryGetValue(key, out var effect) ||
                !effect.Plan.Equals(planned.Plans[key]) ||
                effect.Runtime.RequiresRebuild(candidate[key]));

        if (structureChanged)
        {
            RebuildAll(candidate, planned, width, height);
            return;
        }

        var updated = new List<(
            ActiveEffect Effect,
            Dictionary<InstanceId, IRenderEffectDescriptor> OldOwners)>();
        try
        {
            foreach (var key in planned.OrderedKeys)
            {
                var effect = _active[key];
                var oldOwners =
                    new Dictionary<InstanceId, IRenderEffectDescriptor>(effect.Owners);
                effect.Runtime.UpdateOwners(candidate[key]);
                updated.Add((effect, oldOwners));
            }
        }
        catch
        {
            for (int i = updated.Count - 1; i >= 0; i--)
                updated[i].Effect.Runtime.UpdateOwners(updated[i].OldOwners);
            throw;
        }

        foreach (var key in planned.OrderedKeys)
            _active[key].Owners =
                new Dictionary<InstanceId, IRenderEffectDescriptor>(candidate[key]);
    }

    private void RebuildAll(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> owners,
        PlannedRenderEffectGraph planned,
        int width,
        int height)
    {
        var surfaces = new RenderSurfaceRegistry(_rootSurfaces);
        var replacements = new Dictionary<RenderEffectKey, IRenderEffectRuntime>();
        var createdOrder = new List<RenderEffectKey>(planned.OrderedKeys.Count);
        try
        {
            foreach (var key in planned.OrderedKeys)
            {
                var context = new RenderEffectBuildContext(width, height, _targets, surfaces);
                var runtime = _factories[key.Kind].Create(context, key, owners[key]);
                try
                {
                    ValidateRuntime(key, planned.Plans[key], runtime);
                    for (int i = 0; i < runtime.Outputs.Count; i++)
                        surfaces.Add(planned.Plans[key].OutputSurfaces[i], runtime.Outputs[i].Surface);
                }
                catch
                {
                    DisposeUnattached(runtime);
                    throw;
                }
                replacements.Add(key, runtime);
                createdOrder.Add(key);
            }
        }
        catch
        {
            for (int i = createdOrder.Count - 1; i >= 0; i--)
                DisposeUnattached(replacements[createdOrder[i]]);
            throw;
        }

        var attached = new Dictionary<RenderEffectKey, ActiveEffect>();
        var attachedOrder = new List<RenderEffectKey>(createdOrder.Count);
        var consumed = new HashSet<RenderEffectKey>();
        try
        {
            foreach (var key in createdOrder)
            {
                try
                {
                    attached.Add(key, Attach(
                        replacements[key],
                        planned.Plans[key],
                        owners[key]));
                    attachedOrder.Add(key);
                    consumed.Add(key);
                }
                catch
                {
                    // Attach 已清理当前 runtime。
                    consumed.Add(key);
                    throw;
                }
            }
        }
        catch
        {
            for (int i = attachedOrder.Count - 1; i >= 0; i--)
                Detach(attached[attachedOrder[i]]);
            for (int i = createdOrder.Count - 1; i >= 0; i--)
            {
                var key = createdOrder[i];
                if (!consumed.Contains(key)) DisposeUnattached(replacements[key]);
            }
            throw;
        }

        for (int i = _activeOrder.Count - 1; i >= 0; i--)
            Detach(_active[_activeOrder[i]]);
        _active.Clear();
        _activeOrder.Clear();
        foreach (var key in attachedOrder)
        {
            _active.Add(key, attached[key]);
            _activeOrder.Add(key);
        }
    }

    private ActiveEffect Attach(
        IRenderEffectRuntime runtime,
        RenderEffectPlan plan,
        IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
    {
        var passHandles = new List<RenderPassHandle>(runtime.Passes.Count);
        var sourceHandles = new List<CompositeSourceHandle>(runtime.CompositeSources.Count);
        int attachedPasses = 0;
        try
        {
            foreach (var pass in runtime.Passes)
            {
                passHandles.Add(_graph.AddPass(pass));
                attachedPasses++;
            }
            foreach (var source in runtime.CompositeSources)
                sourceHandles.Add(_graph.AddCompositeSource(source));
        }
        catch
        {
            for (int i = sourceHandles.Count - 1; i >= 0; i--)
                _graph.RemoveCompositeSource(sourceHandles[i]);
            for (int i = passHandles.Count - 1; i >= 0; i--)
                _graph.RemovePass(passHandles[i]);
            for (int i = attachedPasses; i < runtime.Passes.Count; i++)
                runtime.Passes[i].Dispose();
            runtime.Dispose();
            throw;
        }

        return new ActiveEffect(
            runtime,
            plan,
            new Dictionary<InstanceId, IRenderEffectDescriptor>(owners),
            passHandles,
            sourceHandles);
    }

    private void Detach(ActiveEffect effect)
    {
        for (int i = effect.SourceHandles.Count - 1; i >= 0; i--)
            _graph.RemoveCompositeSource(effect.SourceHandles[i]);
        for (int i = effect.PassHandles.Count - 1; i >= 0; i--)
            _graph.RemovePass(effect.PassHandles[i]);
        effect.Runtime.Dispose();
    }

    private static void DisposeUnattached(IRenderEffectRuntime runtime)
    {
        for (int i = runtime.Passes.Count - 1; i >= 0; i--)
            runtime.Passes[i].Dispose();
        runtime.Dispose();
    }

    private static void ValidateRuntime(
        RenderEffectKey key,
        RenderEffectPlan plan,
        IRenderEffectRuntime runtime)
    {
        if (runtime is null)
            throw new InvalidOperationException("Effect factory returned null.");
        if (runtime.Key != key)
            throw new InvalidOperationException(
                $"Effect factory returned runtime '{runtime.Key}' for requested key '{key}'.");
        if (runtime.Passes is null || runtime.CompositeSources is null || runtime.Outputs is null)
            throw new InvalidOperationException(
                $"Effect runtime '{key}' returned a null collection.");
        if (!runtime.Outputs.Select(output => output.Key).SequenceEqual(plan.Outputs))
            throw new InvalidOperationException(
                $"Effect runtime '{key}' outputs do not match its logical plan.");
        if (runtime.Outputs.Any(output => output.Surface is null))
            throw new InvalidOperationException(
                $"Effect runtime '{key}' returned a null render surface.");
        for (int i = 0; i < runtime.Outputs.Count; i++)
        {
            var expected = plan.OutputSurfaces[i];
            var actual = runtime.Outputs[i].Surface;
            if (actual.ColorFormat != expected.ColorFormat)
                throw new InvalidOperationException(
                    $"Effect runtime '{key}' output '{expected.Key}' uses " +
                    $"{actual.ColorFormat}, expected {expected.ColorFormat}.");
        }
    }

    private static Dictionary<InstanceId, IRenderEffectDescriptor> GetOrAdd(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> state,
        RenderEffectKey key)
    {
        if (!state.TryGetValue(key, out var owners))
            state.Add(key, owners = new Dictionary<InstanceId, IRenderEffectDescriptor>());
        return owners;
    }

    private static void RemoveOwner(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> state,
        InstanceId owner,
        RenderEffectKey key)
    {
        if (state.TryGetValue(key, out var owners)) owners.Remove(owner);
    }

    private static void RemoveOwnerFromAll(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> state,
        InstanceId owner)
    {
        foreach (var owners in state.Values) owners.Remove(owner);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = _activeOrder.Count - 1; i >= 0; i--)
            Detach(_active[_activeOrder[i]]);
        _active.Clear();
        _activeOrder.Clear();
        _rootSurfaces.Clear();
        _factories.Clear();
    }

    private sealed class ActiveEffect
    {
        public IRenderEffectRuntime Runtime { get; }
        public RenderEffectPlan Plan { get; }
        public Dictionary<InstanceId, IRenderEffectDescriptor> Owners { get; set; }
        public IReadOnlyList<RenderPassHandle> PassHandles { get; }
        public IReadOnlyList<CompositeSourceHandle> SourceHandles { get; }

        public ActiveEffect(
            IRenderEffectRuntime runtime,
            RenderEffectPlan plan,
            Dictionary<InstanceId, IRenderEffectDescriptor> owners,
            IReadOnlyList<RenderPassHandle> passHandles,
            IReadOnlyList<CompositeSourceHandle> sourceHandles)
        {
            Runtime = runtime;
            Plan = plan;
            Owners = owners;
            PassHandles = passHandles;
            SourceHandles = sourceHandles;
        }
    }
}
