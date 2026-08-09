namespace Engine.DddTests;

using System.Numerics;
using GameEngine.Core.Application.Commands;
using GameEngine.Core.Application.Handlers;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Core.Infrastructure.Windowing;

/// <summary>
/// Phase 1.4 DDD 战术设计验证 Demo（控制台版，无需 OpenGL）。
///
/// 验证项：
///   1. 值对象 (Vector2D / Transform2D / InstanceId / LayerDepth / SceneLayerConfig / BackgroundConfig)
///   2. GameInstance 实体的 LayerName 归属 + 状态变更发出领域事件
///   3. SceneAggregate 聚合根：Viewport / Layer 配置 / Background / Scene 级 Hook / 领域事件
///   4. Command + Handler 模式：AddLayer / SetBackground / Spawn / Destroy
///   5. RenderEffectRequestedEvent：逻辑层声明意图，渲染层捕获执行（解耦验证）
/// </summary>
internal sealed class Program
{
    private static void Main()
    {
        Console.WriteLine("=== Phase 1.4 DDD Tactical Design Smoke Test ===\n");

        // ---------- 1. 值对象测试 ----------
        var v1 = new Vector2D(3f, 4f);
        Console.WriteLine($"1. Vector2D {v1} length={v1.Length():F2} normalized={v1.Normalize()}");

        var t1 = Transform2D.Default;
        var t2 = t1.Translate(new Vector2D(10, 5)).Rotate(MathF.PI / 2);
        Console.WriteLine($"   Transform: default={t1} -> moved+rotated={t2}");

        var layerCfg = new SceneLayerConfig("TestLayer", 500, true);
        Console.WriteLine($"   SceneLayerConfig: {layerCfg}");

        var bgCfg = BackgroundConfig.FromColor(new System.Numerics.Vector4(0.2f, 0.3f, 0.4f, 1f));
        Console.WriteLine($"   BackgroundConfig: {bgCfg}\n");

        // ---------- 2 & 3. 聚合根（完整能力） ----------
        var scene = new SceneAggregate(sceneName: "TestRoom01");
        scene.ViewportWidth = 1920;
        scene.ViewportHeight = 1080;
        scene.Background = BackgroundConfig.FromColor(
            new System.Numerics.Vector4(0.1f, 0.12f, 0.15f, 1f));

        // Scene 级 Hook
        scene.OnStart = () => Console.WriteLine($"[Hook] Scene '{scene.SceneName}' OnStart");
        scene.OnBeforeStep = (dt) => Console.WriteLine($"[Hook] OnBeforeStep dt={dt:F3}");

        Console.WriteLine($"2. SceneAggregate '{scene.SceneName}' id={scene.SceneId:B}");
        Console.WriteLine($"   Viewport={scene.ViewportWidth}x{scene.ViewportHeight}");
        Console.WriteLine($"   Layers count={scene.Layers.Count}");

        // 验证默认图层
        foreach (var l in scene.Layers)
            Console.WriteLine($"   - {l}");

        // 添加自定义图层
        scene.AddLayer("Effects", 500);
        scene.AddLayer("Particles", -500);
        Console.WriteLine($"   After AddLayer: Layers count={scene.Layers.Count}\n");

        // ---------- 4. Command -> Handler → 聚合根 ----------
        // Spawn
        var playerCmd = new SpawnInstanceCommand(
            Scene: scene,
            ObjectTypeName: "Player",
            Position: new Vector2D(100, 200),
            Depth: LayerDepth.Instances);
        var player = SceneCommandHandlers.Handle(playerCmd);

        var enemyCmd = new SpawnInstanceCommand(
            Scene: scene,
            ObjectTypeName: "Enemy",
            Position: new Vector2D(500, 300),
            Depth: LayerDepth.Instances);
        var enemy = SceneCommandHandlers.Handle(enemyCmd);

        // 验证 LayerName 默认分配
        Console.WriteLine($"3. Player LayerName='{player.LayerName}', Enemy LayerName='{enemy.LayerName}'");

        // AddLayer via Command
        var addLayerCmd = new AddLayerCommand(scene, "Foreground", -2000);
        var newLayer = SceneCommandHandlers.Handle(addLayerCmd);
        Console.WriteLine($"   Added layer: {newLayer}");

        // SetLayerVisible via Command
        SceneCommandHandlers.Handle(new SetLayerVisibleCommand(scene, "Effects", false));
        var effectsLayer = scene.FindLayerConfig("Effects");
        Console.WriteLine($"   Effects layer visible? {effectsLayer?.IsVisible}");

        Console.WriteLine($"\n4. Active instances: {scene.ActiveInstances.Count()}");
        foreach (var inst in scene.ActiveInstances)
            Console.WriteLine($"   - {inst} (Layer={inst.LayerName})");

        // 移动 player（触发 InstanceMovedEvent）
        player.MoveTo(new Vector2D(150, 220), scene.RaiseEvent);
        Console.WriteLine($"\n5. After player move: {player}");

        // ---------- 5. Stencil 遮罩命令链路 ----------
        var spotlightCmd = new GameEngine.Features.StencilMasking.Application.ApplySpotlightMaskCommand(
            Scene: scene,
            SpotlightId: player.Id,
            MaskCenter: new Vector2D(150, 220),
            MaskRadius: 80f,
            MaskState: GameEngine.Features.StencilMasking.Domain.StencilMaskState.Spotlight);
        GameEngine.Features.StencilMasking.Application.StencilMaskCommandHandler.Handle(spotlightCmd);

        Console.WriteLine($"\n6. Spotlight command applied to {player.ObjectTypeName}#{player.Id}");

        // ---------- 6. 触发 PerformStep（验证 Hook + 实例 OnStep） ----------
        scene.PerformStep(0.016);
        Console.WriteLine($"\n7. After PerformStep(16ms), active: {scene.ActiveInstances.Count()}");

        // ---------- 7. 检查未提交事件队列 ----------
        Console.WriteLine($"\n8. Uncommitted events: {scene.UncommittedEvents.Count()}");
        foreach (var ev in scene.UncommittedEvents)
        {
            var type = ev.GetType().Name;
            Console.WriteLine($"   [{type}] occurred={ev.OccurredOn:HH:mm:ss.fff}");
        }

        // 标记事件提交
        scene.MarkEventsAsCommitted();
        Console.WriteLine($"   After commit, count: {scene.UncommittedEvents.Count()}");

        // ---------- 8. 销毁实例 ----------
        SceneCommandHandlers.Handle(new DestroyInstanceCommand(scene, enemy.Id));
        Console.WriteLine($"\n9. After destroying enemy, active: {scene.ActiveInstances.Count()}");

        // ---------- 9. Scene.End() ----------
        scene.OnEnd = () => Console.WriteLine("[Hook] Scene OnEnd");
        scene.End();
        Console.WriteLine($"   After End(), non-persistent count: {scene.ActiveInstances.Count()}");

        VerifyInstanceLifecycleAndRenderState();
        VerifyFrameRateAndStatistics();

        Console.WriteLine("\n=== All Phase 1.4 DDD tactical design smoke tests passed ===");
    }

    private static void VerifyInstanceLifecycleAndRenderState()
    {
        var scene = new SceneAggregate("LifecycleRoom");
        var input = new FakeInputProvider();
        var sprites = new FakeSpriteResolver();
        scene.SetInput(input);
        scene.SetSprites(sprites);

        var instance = new LifecycleProbe
        {
            RenderStyle = new RenderStyle(BlendMode.Additive, DepthTest: true, DepthWrite: true),
            Shader = new ShaderRef("probe-shader"),
            Sprite = new SpriteRef("probe-sprite")
        };
        scene.Add(instance);

        scene.PerformInput(new[] { InputKey.W }, new[] { InputKey.Escape });
        scene.PerformStep(0.016);

        var batch = new RecordingSpriteBatch();
        scene.DrawActive(batch);
        scene.DrawGUI(batch);

        Assert(ReferenceEquals(instance.Input, input), "Scene input is injected into instances");
        Assert(batch.BlendMode == BlendMode.Additive, "RenderStyle blend mode is applied");
        Assert(batch.DepthState == (true, true), "RenderStyle depth state is applied");
        Assert(batch.Shader == new ShaderRef("probe-shader"), "ShaderRef is applied");
        Assert(batch.SpriteCommand is { Sprite.Name: "probe-sprite" }, "DrawSelf submits logical Sprite");
        Assert(batch.SpriteCommand is { } draw &&
               draw.Position == new Vector2(3, 4) &&
               draw.Scale == new Vector2(2, -1) &&
               draw.RotationRadians == .5f &&
               draw.Color == new Vector4(.2f, .4f, .6f, .8f),
            "DrawSelf inherits Transform and Color");
        Assert(instance.ImageIndex > 0f, "Sprite animation advances after End Step");

        const string expected =
            "Create,KeyDown:W,KeyUp:Escape,BeginStep,Step,EndStep,BeginDraw,Draw,EndDraw,DrawGUI";
        Assert(string.Join(',', instance.Events) == expected,
            "GameInstance lifecycle and input events are dispatched in order");

        Console.WriteLine("\n10. GameInstance lifecycle / input / render state");
        Console.WriteLine("   [PASS] unified input injection + edge events");
        Console.WriteLine("   [PASS] Begin/Step/End and Draw lifecycle order");
        Console.WriteLine("   [PASS] Blend/Depth/Shader/Sprite render state dispatch");
    }

    private static void VerifyFrameRateAndStatistics()
    {
        var rate = new FrameRateSettings(120d, 60d, vSync: false);
        var options = EngineWindowOptions.Default.WithFrameRate(rate).WithFrameStatistics();
        Assert(options.GetFrameRate() == rate, "Frame-rate settings round-trip through window options");
        Assert(options.FrameStatistics is not null, "Frame statistics are explicitly enabled");
        AssertThrows<ArgumentOutOfRangeException>(
            () => _ = new FrameRateSettings(-1d, 60d, false),
            "Negative frame limits are rejected");

        var collector = new FrameStatisticsCollector(new FrameStatisticsOptions(.5d));
        IFrameStatisticsSink sink = collector;
        sink.RecordUpdate(.25d);
        sink.RecordUpdate(.25d);
        sink.BeginRenderFrame(.25d);
        sink.RecordDrawCall();
        sink.RecordDrawCall();
        sink.RecordDrawCall();
        sink.RecordBatchFlush();
        sink.RecordBatchFlush();
        sink.RecordTextureSwitch();
        for (int i = 0; i < 4; i++) sink.RecordPassExecuted();
        sink.EndRenderFrame();

        Assert(collector.TryCapture(out var snapshot), "Completed frame statistics can be captured");
        Assert(snapshot.FrameNumber == 1, "Frame number advances");
        Assert(snapshot.FramesPerSecond == 4d && snapshot.UpdatesPerSecond == 4d,
            "FPS and UPS use independent elapsed samples");
        Assert(snapshot.DrawCalls == 3 && snapshot.BatchFlushes == 2 &&
               snapshot.TextureSwitches == 1 && snapshot.ActivePasses == 4,
            "Optional render counters preserve the completed frame");

        Console.WriteLine("\n11. Frame-rate control / optional frame statistics");
        Console.WriteLine("   [PASS] startup settings + strict validation");
        Console.WriteLine("   [PASS] FPS/UPS + Draw/Flush/Texture/Pass counters");
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"[FAIL] {message}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"[FAIL] {message}");
    }

    private sealed class LifecycleProbe : GameInstance
    {
        public List<string> Events { get; } = new();

        public LifecycleProbe()
        {
            Transform = new Transform2D(new Vector2D(3, 4), .5f, new Vector2D(2, -1));
            Color = new Vector4(.2f, .4f, .6f, .8f);
        }

        public override void OnCreate() => Events.Add("Create");
        public override void OnKeyDown(InputKey key) => Events.Add($"KeyDown:{key}");
        public override void OnKeyUp(InputKey key) => Events.Add($"KeyUp:{key}");
        public override void OnBeginStep(double deltaTime) => Events.Add("BeginStep");
        public override void OnStep(double deltaTime) => Events.Add("Step");
        public override void OnEndStep(double deltaTime) => Events.Add("EndStep");
        public override void OnBeginDraw(ISpriteBatch batch) => Events.Add("BeginDraw");
        public override void OnDraw(ISpriteBatch batch)
        {
            DrawSelf(batch);
            Events.Add("Draw");
        }
        public override void OnEndDraw(ISpriteBatch batch) => Events.Add("EndDraw");
        public override void OnDrawGUI(ISpriteBatch batch) => Events.Add("DrawGUI");
    }

    private sealed class FakeInputProvider : IInputProvider
    {
        public bool IsKeyDown(InputKey key) => false;
        public Vector2D MousePosition => Vector2D.Zero;
        public float MouseScrollDelta => 0;
        public bool IsMouseButtonDown(GameEngine.Core.Domain.Input.MouseButton button) => false;
    }

    private sealed class RecordingSpriteBatch : ISpriteBatch
    {
        public BlendMode BlendMode { get; private set; }
        public (bool Test, bool Write) DepthState { get; private set; }
        public ShaderRef? Shader { get; private set; }
        public SpriteDrawCommand? SpriteCommand { get; private set; }

        public void Begin() { }
        public void End() { }
        public void Flush() { }
        public void Draw(uint textureHandle, Vector2 position, Vector2 size, Vector4 color,
            Vector4 uvBounds = default) { }
        public void DrawSpriteCommand(in SpriteDrawCommand command) => SpriteCommand = command;
        public bool TryGetSpriteMetadata(SpriteRef sprite, out SpriteMetadata metadata)
        {
            metadata = new SpriteMetadata(new Vector2(16), new Vector2(8), 4, 4f);
            return !sprite.IsEmpty;
        }
        public void SetBlendMode(BlendMode mode) => BlendMode = mode;
        public void SetDepthState(bool depthTest, bool depthWrite) =>
            DepthState = (depthTest, depthWrite);
        public void SetShader(ShaderRef? shader) => Shader = shader;
    }

    private sealed class FakeSpriteResolver : ISpriteResolver
    {
        public bool TryGetMetadata(SpriteRef sprite, out SpriteMetadata metadata)
        {
            metadata = new SpriteMetadata(new Vector2(16), new Vector2(8), 4, 4f);
            return !sprite.IsEmpty;
        }

        public bool TryResolve(SpriteRef sprite, int subImage, out ResolvedSpriteFrame frame)
        {
            frame = new ResolvedSpriteFrame(1u, new Vector2(16), new Vector2(8),
                new Vector4(0, 0, 1, 1));
            return !sprite.IsEmpty;
        }
    }
}
