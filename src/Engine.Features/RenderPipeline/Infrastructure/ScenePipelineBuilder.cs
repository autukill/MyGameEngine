namespace GameEngine.Features.RenderPipeline.Infrastructure;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;

/// <summary>在 Step/Draw 边界把领域效果状态差量映射为 RenderPass 图。</summary>
public sealed class ScenePipelineBuilder : IDisposable
{
    private readonly IRenderEffectGraphEditor _graph;
    private readonly IRenderTargetPool _targets;
    private readonly Dictionary<string, IRenderEffectFactory> _factories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<RenderEffectKey, ActiveEffect> _active = new();
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
                            $"No render effect factory is registered for '{requested.Descriptor.Key.Kind}'.");
                    GetOrAdd(candidate, requested.Descriptor.Key)[requested.OwnerId] = requested.Descriptor;
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

        foreach (var empty in candidate.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray())
            candidate.Remove(empty);

        ValidateCandidate(candidate);
        Reconcile(candidate, _width, _height);
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (width == _width && height == _height) return;

        var owners = CloneOwnerState();
        RebuildAll(owners, width, height);
        _width = width;
        _height = height;
        _targets.TrimExceptSize(width, height);
    }

    private Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> CloneOwnerState() =>
        _active.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<InstanceId, IRenderEffectDescriptor>(pair.Value.Owners));

    private void ValidateCandidate(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> candidate)
    {
        foreach (var (key, owners) in candidate)
        {
            if (!_factories.TryGetValue(key.Kind, out var factory))
                throw new InvalidOperationException(
                    $"No render effect factory is registered for '{key.Kind}'.");
            factory.Validate(key, owners);
        }
    }

    private void Reconcile(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> candidate,
        int width,
        int height)
    {
        if (candidate.Any(pair =>
                _active.TryGetValue(pair.Key, out var active) &&
                active.Runtime.RequiresRebuild(pair.Value)))
        {
            RebuildAll(candidate, width, height);
            return;
        }

        var context = new RenderEffectBuildContext(width, height, _targets);
        var created = new Dictionary<RenderEffectKey, IRenderEffectRuntime>();
        try
        {
            foreach (var (key, owners) in candidate)
            {
                if (_active.ContainsKey(key)) continue;
                var runtime = _factories[key.Kind].Create(context, key, owners);
                ValidateRuntime(key, runtime);
                created.Add(key, runtime);
            }
        }
        catch
        {
            foreach (var runtime in created.Values) DisposeUnattached(runtime);
            throw;
        }

        var updated = new List<(ActiveEffect Effect, Dictionary<InstanceId, IRenderEffectDescriptor> OldOwners)>();
        var attached = new Dictionary<RenderEffectKey, ActiveEffect>();
        try
        {
            foreach (var (key, owners) in candidate)
            {
                if (!_active.TryGetValue(key, out var current)) continue;
                var oldOwners = new Dictionary<InstanceId, IRenderEffectDescriptor>(current.Owners);
                current.Runtime.UpdateOwners(owners);
                updated.Add((current, oldOwners));
            }

            foreach (var (key, runtime) in created.ToArray())
            {
                try
                {
                    attached.Add(key, Attach(runtime, candidate[key]));
                }
                catch
                {
                    // Attach 已完整清理当前 runtime，避免外层再次释放。
                    created.Remove(key);
                    throw;
                }
            }
        }
        catch
        {
            foreach (var effect in attached.Values) Detach(effect);
            foreach (var (key, runtime) in created)
                if (!attached.ContainsKey(key)) DisposeUnattached(runtime);
            for (int i = updated.Count - 1; i >= 0; i--)
                updated[i].Effect.Runtime.UpdateOwners(updated[i].OldOwners);
            throw;
        }

        foreach (var key in _active.Keys.Where(key => !candidate.ContainsKey(key)).ToArray())
        {
            Detach(_active[key]);
            _active.Remove(key);
        }

        foreach (var (key, owners) in candidate)
        {
            if (attached.TryGetValue(key, out var newEffect))
                _active.Add(key, newEffect);
            else
                _active[key].Owners = new Dictionary<InstanceId, IRenderEffectDescriptor>(owners);
        }
    }

    private void RebuildAll(
        Dictionary<RenderEffectKey, Dictionary<InstanceId, IRenderEffectDescriptor>> owners,
        int width,
        int height)
    {
        var context = new RenderEffectBuildContext(width, height, _targets);
        var replacements = new Dictionary<RenderEffectKey, IRenderEffectRuntime>();
        try
        {
            foreach (var (key, effectOwners) in owners)
            {
                var runtime = _factories[key.Kind].Create(context, key, effectOwners);
                ValidateRuntime(key, runtime);
                replacements.Add(key, runtime);
            }
        }
        catch
        {
            foreach (var runtime in replacements.Values) DisposeUnattached(runtime);
            throw;
        }

        var attached = new Dictionary<RenderEffectKey, ActiveEffect>();
        try
        {
            foreach (var (key, runtime) in replacements.ToArray())
            {
                try
                {
                    attached.Add(key, Attach(runtime, owners[key]));
                }
                catch
                {
                    replacements.Remove(key);
                    throw;
                }
            }
        }
        catch
        {
            foreach (var effect in attached.Values) Detach(effect);
            foreach (var (key, runtime) in replacements)
                if (!attached.ContainsKey(key)) DisposeUnattached(runtime);
            throw;
        }

        foreach (var effect in _active.Values) Detach(effect);
        _active.Clear();
        foreach (var (key, effect) in attached) _active.Add(key, effect);
    }

    private ActiveEffect Attach(
        IRenderEffectRuntime runtime,
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
        foreach (var pass in runtime.Passes) pass.Dispose();
        runtime.Dispose();
    }

    private static void ValidateRuntime(RenderEffectKey key, IRenderEffectRuntime runtime)
    {
        if (runtime is null) throw new InvalidOperationException("Effect factory returned null.");
        if (runtime.Key != key)
            throw new InvalidOperationException(
                $"Effect factory returned runtime '{runtime.Key}' for requested key '{key}'.");
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
        foreach (var effect in _active.Values) Detach(effect);
        _active.Clear();
        _factories.Clear();
    }

    private sealed class ActiveEffect
    {
        public IRenderEffectRuntime Runtime { get; }
        public Dictionary<InstanceId, IRenderEffectDescriptor> Owners { get; set; }
        public IReadOnlyList<RenderPassHandle> PassHandles { get; }
        public IReadOnlyList<CompositeSourceHandle> SourceHandles { get; }

        public ActiveEffect(
            IRenderEffectRuntime runtime,
            Dictionary<InstanceId, IRenderEffectDescriptor> owners,
            IReadOnlyList<RenderPassHandle> passHandles,
            IReadOnlyList<CompositeSourceHandle> sourceHandles)
        {
            Runtime = runtime;
            Owners = owners;
            PassHandles = passHandles;
            SourceHandles = sourceHandles;
        }
    }
}
