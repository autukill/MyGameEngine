namespace RenderPipeline.Tests;

using Silk.NET.OpenGL;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.RenderPipeline.Infrastructure;

/// <summary>
/// RenderPipeline 切片的控制台冒烟测试（无 GL 上下文，仅验证值对象纯逻辑）。
///
/// 验证项：
///   1. BlendState 预设（AlphaBlend / Additive / Opaque / ColorMaskDisabled）
///   2. DepthStencilState 预设（None / StencilWrite / StencilTest / StencilTestNotEqual）
///   3. ViewportRect 预设 + 像素换算
///   4. LayerRenderState 默认 + 覆盖
///   5. 值对象零 GC：可作字典 Key（状态指纹）
/// </summary>
internal static class Program
{
    private static int _failures;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }

    private static void Main()
    {
        Console.WriteLine("=== RenderPipeline Feature Smoke Test ===\n");

        // ---------- 1. BlendState ----------
        Console.WriteLine("1. BlendState presets");
        Check(BlendState.AlphaBlend.EnableBlend &&
              BlendState.AlphaBlend.SrcFactor == BlendingFactor.SrcAlpha &&
              BlendState.AlphaBlend.DstFactor == BlendingFactor.OneMinusSrcAlpha,
            "AlphaBlend = SrcAlpha / OneMinusSrcAlpha");
        Check(BlendState.Additive.DstFactor == BlendingFactor.One,
            "Additive = SrcAlpha / One");
        Check(!BlendState.Opaque.EnableBlend, "Opaque disables blend");
        Check(!BlendState.ColorMaskDisabled.WriteR &&
              !BlendState.ColorMaskDisabled.WriteA,
            "ColorMaskDisabled masks all color writes");

        // ---------- 2. DepthStencilState ----------
        Console.WriteLine("2. DepthStencilState presets");
        Check(!DepthStencilState.None.StencilTestEnable, "None disables stencil");
        Check(DepthStencilState.StencilWrite().StencilFunc == StencilFunction.Always &&
              DepthStencilState.StencilWrite().StencilPass == StencilOp.Replace,
            "StencilWrite = Always + Replace");
        Check(DepthStencilState.StencilTest().StencilFunc == StencilFunction.Equal,
            "StencilTest = Equal");
        Check(DepthStencilState.StencilTestNotEqual().StencilFunc == StencilFunction.Notequal,
            "StencilTestNotEqual = Notequal");

        // ---------- 3. ViewportRect ----------
        Console.WriteLine("3. ViewportRect");
        Check(ViewportRect.FullScreen == new ViewportRect(0, 0, 1, 1), "FullScreen rect");
        var px = ViewportRect.BottomHalf.ToPixels(800, 600);
        Check(px == (0, 300, 800, 300), "BottomHalf pixels @800x600");
        var q = ViewportRect.TopRightQuarter.ToPixels(400, 200);
        Check(q == (300, 0, 100, 50), "TopRightQuarter pixels @400x200");

        // ---------- 4. LayerRenderState ----------
        Console.WriteLine("4. LayerRenderState");
        Check(LayerRenderState.Default.BlendOverride is null, "Default has no override");
        Check(LayerRenderState.AdditiveBlend.BlendOverride == BlendState.Additive,
            "AdditiveBlend override set");
        Check(LayerRenderState.UI.DepthStencilOverride is { DepthTestEnable: false },
            "UI overrides depth off");

        // ---------- 5. 值对象作字典 Key（状态指纹去重） ----------
        Console.WriteLine("5. Value-object state fingerprint");
        var states = new Dictionary<BlendState, string>();
        states[BlendState.AlphaBlend] = "alpha";
        states[BlendState.Additive] = "additive";
        states[BlendState.Opaque] = "opaque";
        states[BlendState.AlphaBlend] = "alpha-again"; // 覆盖同 Key
        Check(states.Count == 3, "BlendState works as dictionary key (dedup)");

        var stencils = new HashSet<DepthStencilState>
        {
            DepthStencilState.StencilWrite(),
            DepthStencilState.StencilWrite(2),
            DepthStencilState.StencilWrite(),
        };
        Check(stencils.Count == 2, "DepthStencilState works as set element");

        TestResourcePoolCore();
        TestRenderEffectGraphPlanner();
        TestScenePipelineBuilder();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All RenderPipeline smoke tests passed ==="
            : $"=== {_failures} RenderPipeline test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void TestResourcePoolCore()
    {
        Console.WriteLine("6. RenderTarget pool ownership core");
        int created = 0;
        var pool = new ResourcePoolCore<string, FakeResource>(
            key => new FakeResource($"{key}-{++created}"),
            resource => resource.Dispose());

        var first = pool.Rent("rgba");
        pool.Return(first);
        var reused = pool.Rent("rgba");
        Check(ReferenceEquals(first, reused), "Same descriptor reuses returned resource");
        var depth = pool.Rent("depth");
        Check(!ReferenceEquals(reused, depth), "Different descriptors stay isolated");

        bool foreignRejected = false;
        try { pool.Return(new FakeResource("foreign")); }
        catch (ArgumentException) { foreignRejected = true; }
        Check(foreignRejected, "Foreign resource is rejected");

        pool.Return(reused);
        bool doubleReturnRejected = false;
        try { pool.Return(reused); }
        catch (InvalidOperationException) { doubleReturnRejected = true; }
        Check(doubleReturnRejected, "Duplicate return is rejected by pool core");

        pool.TrimAvailable(key => key == "depth");
        Check(first.DisposeCount == 1, "Trim disposes obsolete available resource");
        pool.Dispose();
        Check(depth.DisposeCount == 1, "Pool disposal releases leased resource once");
        pool.Dispose();
        Check(depth.DisposeCount == 1, "Pool disposal is idempotent");
    }

    private static void TestScenePipelineBuilder()
    {
        Console.WriteLine("8. Dynamic effect owner reconciliation");
        var graph = new FakeGraphEditor();
        var targets = new FakeTargetPool();
        var factory = new FakeEffectFactory("test");
        var builder = new ScenePipelineBuilder(graph, targets, 800, 600);
        builder.RegisterFactory(factory);
        var key = new RenderEffectKey("test", "main");
        var ownerA = InstanceId.New();
        var ownerB = InstanceId.New();

        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 1, 7))
        });
        Check(builder.ActiveEffectCount == 1 && factory.CreateCount == 1,
            "First owner creates one runtime");

        var firstRuntime = factory.LastRuntime!;
        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 2, 7)),
            new RenderEffectRequestedEvent(ownerB, new FakeDescriptor(key, 3, 7))
        });
        Check(factory.CreateCount == 1 && builder.GetOwnerCount(key) == 2,
            "Updates and second owner share the existing runtime");
        Check(firstRuntime.OwnerCount == 2 && firstRuntime.UpdateCount >= 2,
            "Owner descriptors are pushed into the runtime");

        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectReleasedEvent(ownerA, key)
        });
        Check(builder.ActiveEffectCount == 1 && builder.GetOwnerCount(key) == 1,
            "Releasing one owner keeps shared effect alive");

        builder.ApplyEvents(new IDomainEvent[]
        {
            new InstanceActivationChangedEvent(ownerB, false)
        });
        Check(builder.ActiveEffectCount == 0 && firstRuntime.DisposeCount == 1,
            "Last inactive owner removes and disposes the effect");

        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 1, 7)),
            new RenderEffectReleasedEvent(ownerA, key)
        });
        Check(builder.ActiveEffectCount == 0, "Request then release follows event order");

        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectReleasedEvent(ownerA, key),
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 4, 7))
        });
        Check(builder.ActiveEffectCount == 1, "Release then request reacquires effect");

        var beforeUnknown = factory.CreateCount;
        bool unknownRejected = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(ownerB,
                    new FakeDescriptor(new RenderEffectKey("missing", "main"), 1, 7))
            });
        }
        catch (InvalidOperationException) { unknownRejected = true; }
        Check(unknownRejected && factory.CreateCount == beforeUnknown && builder.ActiveEffectCount == 1,
            "Unknown factory fails without mutating existing graph");

        bool conflictRejected = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(ownerB, new FakeDescriptor(key, 5, 8))
            });
        }
        catch (InvalidOperationException) { conflictRejected = true; }
        Check(conflictRejected && builder.GetOwnerCount(key) == 1,
            "Conflicting shared configuration is rejected atomically");

        var beforeMissingRuntime = factory.LastRuntime!;
        int beforeMissingCreates = factory.CreateCount;
        bool missingSurfaceRejected = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(
                    key,
                    6,
                    7,
                    new RenderSurfaceKey("missing", "main", "color")))
            });
        }
        catch (InvalidOperationException) { missingSurfaceRejected = true; }
        Check(missingSurfaceRejected && factory.CreateCount == beforeMissingCreates &&
              beforeMissingRuntime.DisposeCount == 0 && builder.ActiveEffectCount == 1,
            "Missing planned input is rejected before mutating the active graph");

        var inPlaceRuntime = factory.LastRuntime!;
        int beforeInPlaceUpdates = inPlaceRuntime.UpdateCount;
        int beforeInPlaceCreates = factory.CreateCount;
        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 6, 7))
        });
        Check(factory.CreateCount == beforeInPlaceCreates &&
              ReferenceEquals(factory.LastRuntime, inPlaceRuntime) &&
              inPlaceRuntime.UpdateCount == beforeInPlaceUpdates + 1,
            "Non-structural descriptor updates stay on the existing runtime");

        int beforeStructuralCreates = factory.CreateCount;
        var beforeStructuralRuntime = factory.LastRuntime!;
        builder.ApplyEvents(new IDomainEvent[]
        {
            new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 6, 9))
        });
        Check(factory.CreateCount == beforeStructuralCreates + 1 &&
              beforeStructuralRuntime.DisposeCount == 1 && graph.PassCount == 1,
            "Runtime-requested structural change atomically rebuilds the effect graph");

        var stableRuntime = factory.LastRuntime!;
        factory.FailNextCreate = true;
        bool rebuildFailureSurfaced = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 6, 10))
            });
        }
        catch (InvalidOperationException) { rebuildFailureSurfaced = true; }
        Check(rebuildFailureSurfaced && stableRuntime.DisposeCount == 0 &&
              builder.ActiveEffectCount == 1 && graph.PassCount == 1,
            "Structural rebuild failure preserves the prior runtime atomically");

        graph.FailNextAddPass = true;
        bool attachFailureSurfaced = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(ownerA, new FakeDescriptor(key, 6, 11))
            });
        }
        catch (InvalidOperationException) { attachFailureSurfaced = true; }
        var failedAttachRuntime = factory.LastRuntime!;
        Check(attachFailureSurfaced && stableRuntime.DisposeCount == 0 &&
              failedAttachRuntime.DisposeCount == 1 &&
              builder.ActiveEffectCount == 1 && graph.PassCount == 1,
            "Graph attachment failure preserves the prior runtime atomically");

        factory.FailNextCreate = true;
        bool creationFailureSurfaced = false;
        try
        {
            builder.ApplyEvents(new IDomainEvent[]
            {
                new RenderEffectRequestedEvent(
                    ownerB,
                    new FakeDescriptor(new RenderEffectKey("test", "secondary"), 1, 7))
            });
        }
        catch (InvalidOperationException) { creationFailureSurfaced = true; }
        Check(creationFailureSurfaced && builder.ActiveEffectCount == 1 && graph.PassCount == 1,
            "Factory creation failure preserves the previous graph atomically");

        var beforeResizeRuntime = stableRuntime;
        int beforeResizeCreates = factory.CreateCount;
        builder.Resize(1024, 768);
        Check(factory.CreateCount == beforeResizeCreates + 1 && beforeResizeRuntime.DisposeCount == 1,
            "Resize recreates active runtime and releases the previous one");
        Check(targets.LastTrimSize == (1024, 768), "Resize trims obsolete target sizes");

        builder.ApplyEvents(new IDomainEvent[] { new InstanceDestroyedEvent(ownerA, "Test") });
        Check(builder.ActiveEffectCount == 0, "Destroyed owner releases all effects");
        builder.Dispose();
        builder.Dispose();
        Check(graph.PassCount == 0, "Builder disposal is idempotent and detaches all passes");
    }

    private static void TestRenderEffectGraphPlanner()
    {
        Console.WriteLine("7. Logical render surface dependency planning");
        var keyA = new RenderEffectKey("a", "main");
        var keyB = new RenderEffectKey("b", "main");
        var keyC = new RenderEffectKey("c", "main");
        var surfaceA = RenderSurfaceKey.FromEffect(keyA, "color");
        var surfaceB = RenderSurfaceKey.FromEffect(keyB, "color");
        var surfaceC = RenderSurfaceKey.FromEffect(keyC, "color");

        var graph = RenderEffectGraphPlanner.Plan(
            new Dictionary<RenderEffectKey, RenderEffectPlan>
            {
                [keyC] = new RenderEffectPlan(
                    keyC, new[] { RenderSurfaceKey.SceneColor }, new[] { surfaceC }),
                [keyB] = new RenderEffectPlan(keyB, new[] { surfaceA }, new[] { surfaceB }),
                [keyA] = new RenderEffectPlan(
                    keyA, new[] { RenderSurfaceKey.SceneColor }, new[] { surfaceA })
            },
            new[] { RenderSurfaceKey.SceneColor });
        Check(graph.OrderedKeys.SequenceEqual(new[] { keyA, keyB, keyC }),
            "Dependencies are topological and independent effects use stable keys");

        CheckThrows<InvalidOperationException>(() => RenderEffectGraphPlanner.Plan(
                new Dictionary<RenderEffectKey, RenderEffectPlan>
                {
                    [keyA] = new RenderEffectPlan(
                        keyA, new[] { surfaceB }, new[] { surfaceA })
                },
                new[] { RenderSurfaceKey.SceneColor }),
            "Missing logical input is rejected before runtime creation");

        CheckThrows<InvalidOperationException>(() => RenderEffectGraphPlanner.Plan(
                new Dictionary<RenderEffectKey, RenderEffectPlan>
                {
                    [keyA] = new RenderEffectPlan(keyA, outputs: new[] { surfaceA }),
                    [keyB] = new RenderEffectPlan(keyB, outputs: new[] { surfaceA })
                },
                Array.Empty<RenderSurfaceKey>()),
            "Duplicate logical output producer is rejected");

        CheckThrows<InvalidOperationException>(() => RenderEffectGraphPlanner.Plan(
                new Dictionary<RenderEffectKey, RenderEffectPlan>
                {
                    [keyA] = new RenderEffectPlan(keyA, new[] { surfaceB }, new[] { surfaceA }),
                    [keyB] = new RenderEffectPlan(keyB, new[] { surfaceA }, new[] { surfaceB })
                },
                Array.Empty<RenderSurfaceKey>()),
            "Logical surface dependency cycle is rejected");

        CheckThrows<InvalidOperationException>(() => RenderEffectGraphPlanner.Plan(
                new Dictionary<RenderEffectKey, RenderEffectPlan>
                {
                    [keyA] = new RenderEffectPlan(keyA, outputs: new[] { surfaceA })
                },
                new[] { surfaceA }),
            "Effects cannot replace a borrowed root surface");

        CheckThrows<InvalidOperationException>(() => RenderEffectGraphPlanner.Plan(
                new Dictionary<RenderEffectKey, RenderEffectPlan>
                {
                    [keyA] = new RenderEffectPlan(keyA, outputs: new[] { surfaceB })
                },
                Array.Empty<RenderSurfaceKey>()),
            "Effects cannot publish a surface owned by another effect key");
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (T)
        {
            Check(true, name);
        }
    }

    private sealed class FakeResource : IDisposable
    {
        public string Name { get; }
        public int DisposeCount { get; private set; }
        public FakeResource(string name) => Name = name;
        public void Dispose() => DisposeCount++;
    }

    private sealed record FakeDescriptor(
        RenderEffectKey Key,
        int Value,
        int SharedConfiguration,
        RenderSurfaceKey? Input = null) : IRenderEffectDescriptor;

    private sealed class FakeEffectFactory : IRenderEffectFactory
    {
        public string Kind { get; }
        public int CreateCount { get; private set; }
        public FakeRuntime? LastRuntime { get; private set; }
        public bool FailNextCreate { get; set; }
        public FakeEffectFactory(string kind) => Kind = kind;

        public RenderEffectPlan Plan(
            RenderEffectKey key,
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            var descriptors = owners.Values.Cast<FakeDescriptor>().ToArray();
            if (descriptors.Select(value => value.SharedConfiguration).Distinct().Count() > 1)
                throw new InvalidOperationException("Conflicting shared configuration.");
            if (descriptors.Select(value => value.Input).Distinct().Count() > 1)
                throw new InvalidOperationException("Conflicting logical input.");
            return descriptors[0].Input is { } input
                ? new RenderEffectPlan(key, new[] { input })
                : new RenderEffectPlan(key);
        }

        public IRenderEffectRuntime Create(
            in RenderEffectBuildContext context,
            RenderEffectKey key,
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            CreateCount++;
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new InvalidOperationException("Synthetic creation failure.");
            }
            var runtime = new FakeRuntime(key);
            runtime.UpdateOwners(owners);
            LastRuntime = runtime;
            return runtime;
        }
    }

    private sealed class FakeRuntime : IRenderEffectRuntime
    {
        public RenderEffectKey Key { get; }
        public IReadOnlyList<RenderPass> Passes { get; }
        public IReadOnlyList<RenderEffectCompositeSource> CompositeSources { get; } =
            Array.Empty<RenderEffectCompositeSource>();
        public IReadOnlyList<RenderEffectOutput> Outputs { get; } =
            Array.Empty<RenderEffectOutput>();
        public int OwnerCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int DisposeCount { get; private set; }
        private int _sharedConfiguration;

        public FakeRuntime(RenderEffectKey key)
        {
            Key = key;
            Passes = new RenderPass[] { new FakePass($"fake:{key}") };
        }

        public void UpdateOwners(IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners)
        {
            OwnerCount = owners.Count;
            _sharedConfiguration = owners.Values.Cast<FakeDescriptor>().First().SharedConfiguration;
            UpdateCount++;
        }

        public bool RequiresRebuild(
            IReadOnlyDictionary<InstanceId, IRenderEffectDescriptor> owners) =>
            owners.Values.Cast<FakeDescriptor>().First().SharedConfiguration != _sharedConfiguration;

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakePass : RenderPass
    {
        public FakePass(string name) : base(name) { }
        public override RenderTarget2D? Output => null;
        public override IEnumerable<RenderTarget2D> Inputs => Array.Empty<RenderTarget2D>();
        public override void Execute(in RenderPassContext ctx) { }
    }

    private sealed class FakeGraphEditor : IRenderEffectGraphEditor
    {
        private long _nextPass;
        private readonly Dictionary<RenderPassHandle, RenderPass> _passes = new();
        public int PassCount => _passes.Count;
        public bool FailNextAddPass { get; set; }
        public RenderPassHandle AddPass(RenderPass pass)
        {
            if (FailNextAddPass)
            {
                FailNextAddPass = false;
                throw new InvalidOperationException("Synthetic graph attachment failure.");
            }
            var handle = new RenderPassHandle(++_nextPass);
            _passes.Add(handle, pass);
            return handle;
        }
        public bool RemovePass(RenderPassHandle handle)
        {
            if (!_passes.Remove(handle, out var pass)) return false;
            pass.Dispose();
            return true;
        }
        public CompositeSourceHandle AddCompositeSource(in RenderEffectCompositeSource source) =>
            throw new InvalidOperationException("Fake runtime has no composite sources.");
        public bool RemoveCompositeSource(CompositeSourceHandle handle) => false;
    }

    private sealed class FakeTargetPool : IRenderTargetPool
    {
        public (int Width, int Height) LastTrimSize { get; private set; }
        public RenderTargetLease Rent(RenderTargetDescriptor descriptor) =>
            throw new InvalidOperationException("Fake factory must not rent GPU targets.");
        public void TrimExceptSize(int width, int height) => LastTrimSize = (width, height);
        public void Dispose() { }
    }
}
