namespace Engine.DddTests;

using System.Numerics;
using GameEngine.Core.Application.Commands;
using GameEngine.Core.Application.Handlers;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Gameplay;
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
        VerifyMaterialParameterBlocks();
        VerifyGameplayAuthoringExperience();
        VerifyPrefabCollisionAndSceneTransition();

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
            Material = new MaterialRef("probe-material"),
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
        Assert(batch.Material == new MaterialRef("probe-material"),
            "MaterialRef takes precedence over ShaderRef");
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

    private static void VerifyMaterialParameterBlocks()
    {
        var parameters = new MaterialParameterBlock(
            ShaderUniformDefinition.Float("uGain"),
            ShaderUniformDefinition.Int("uMode"),
            ShaderUniformDefinition.Vector2("uDirection"),
            ShaderUniformDefinition.Vector4("uTint"));

        parameters
            .SetFloat("uGain", 1.25f)
            .SetInt("uMode", 2)
            .SetVector2("uDirection", new Vector2(1, -1))
            .SetVector4("uTint", new Vector4(.2f, .4f, .6f, .8f));
        long revision = parameters.Revision;
        parameters.SetFloat("uGain", 1.25f);

        Assert(parameters.GetFloat("uGain") == 1.25f &&
               parameters.GetInt("uMode") == 2 &&
               parameters.GetVector2("uDirection") == new Vector2(1, -1) &&
               parameters.GetVector4("uTint") == new Vector4(.2f, .4f, .6f, .8f),
            "Typed material values round-trip");
        Assert(parameters.Revision == revision,
            "Assigning an unchanged material value does not advance its revision");
        AssertThrows<InvalidOperationException>(
            () => parameters.SetInt("uGain", 1),
            "Uniform type mismatches are rejected");
        AssertThrows<KeyNotFoundException>(
            () => parameters.SetFloat("uMissing", 1),
            "Undeclared uniforms are rejected");
        AssertThrows<ArgumentException>(
            () => _ = new MaterialParameterBlock(
                ShaderUniformDefinition.Float("uGain"),
                ShaderUniformDefinition.Float("uGain")),
            "Duplicate uniform declarations are rejected");
        AssertThrows<ArgumentException>(
            () => _ = ShaderUniformDefinition.Float("uProjection"),
            "Engine-owned uniforms are reserved");

        var material = new MaterialRef("probe.material");
        var gain = new MaterialParameterRef<float>(material, "uGain");
        var direction = new MaterialParameterRef<Vector2>(material, "uDirection");
        Assert(gain.Material == material && gain.Name == "uGain" &&
               direction.Material == material && direction.Name == "uDirection",
            "Strongly typed parameter references preserve material ownership and value type");
        AssertThrows<ArgumentException>(
            () => _ = new MaterialParameterRef<float>(MaterialRef.Empty, "uGain"),
            "Typed parameters reject an empty material owner");
        AssertThrows<NotSupportedException>(
            () => _ = new MaterialParameterRef<double>(material, "uGain"),
            "Typed parameters reject unsupported CLR value types");

        Console.WriteLine("\n12. Typed material parameter blocks");
        Console.WriteLine("   [PASS] strict schema + typed values + change revision");
        Console.WriteLine("   [PASS] engine-owned uniforms remain protected");
        Console.WriteLine("   [PASS] logical MaterialParameterRef<T> ownership + supported types");
    }

    private static void VerifyGameplayAuthoringExperience()
    {
        var scene = new SceneAggregate("GameplayAuthoring");
        var input = new FakeInputProvider(
            down: [InputKey.D],
            pressed: [InputKey.Space],
            released: [InputKey.E]);
        scene.SetInput(input);
        var player = scene.Add(new GameplayProbe());

        scene.PerformStep(.01d);
        Assert(player.Position == new Vector2D(10, 0) && player.Rotation == .5f &&
               player.Scale == new Vector2D(2, 3),
            "Position, rotation, scale, and digital axis helpers author gameplay directly");
        Assert(player.Pressed && player.Released,
            "Non-null gameplay input exposes current-frame press and release edges");
        Assert(player.FoundAfterQueue == 0 && scene.FindByType<GameplayChild>().Count() == 1,
            "Spawn is invisible during the requesting Step and commits at the frame boundary");
        GameplayChild child = scene.FindByType<GameplayChild>().Single();
        Assert(child.Created && child.Steps == 0,
            "A boundary-spawned instance runs Create immediately but starts Step next frame");

        scene.PerformStep(.01d);
        Assert(player.AlarmCount == 1 && player.FoundOnSecondStep == child &&
               child.Steps == 1 && child.Destroyed &&
               !scene.FindByType<GameplayChild>().Any(),
            "Alarms fire before Begin Step and queued Find/Destroy preserve deterministic order");

        scene.PerformStep(.01d);
        Assert(!scene.FindByType<GameplayProbe>().Any(),
            "DestroySelf removes the owner at the same frame boundary");
        AssertThrows<InvalidOperationException>(
            () => new GameplayProbe().RequestDestroy(),
            "Gameplay operations reject instances that do not belong to a Scene");
        AssertThrows<ArgumentOutOfRangeException>(
            () => new GameplayProbe().SetAlarm(GameplayProbe.TickAlarm, -1d),
            "Alarm delays reject negative values");

        var pauseScene = new SceneAggregate("PausedAlarms");
        var paused = pauseScene.Add(new AlarmProbe());
        paused.SetActive(false, pauseScene.RaiseEvent);
        pauseScene.PerformStep(1d);
        Assert(paused.AlarmCount == 0 && paused.IsAlarmSet(AlarmProbe.TickAlarm),
            "Inactive instances pause their alarms");
        paused.SetActive(true, pauseScene.RaiseEvent);
        pauseScene.PerformStep(.01d);
        Assert(paused.AlarmCount == 1,
            "A reactivated instance resumes its pending alarms");

        Console.WriteLine("\n13. Gameplay authoring experience");
        Console.WriteLine("   [PASS] transform + input conveniences");
        Console.WriteLine("   [PASS] instance-scoped Spawn/Find/DestroySelf");
        Console.WriteLine("   [PASS] deterministic frame-boundary mutations + lightweight alarms");
    }

    private static void VerifyPrefabCollisionAndSceneTransition()
    {
        var projectilePrefab = new PrefabRef<PrefabProjectile>("test.projectile");
        var factory = new InstanceFactory();
        factory.Register(projectilePrefab, spawn => new PrefabProjectile(spawn.Position));
        AssertThrows<ArgumentException>(
            () => factory.Register(projectilePrefab, spawn => new PrefabProjectile(spawn.Position)),
            "Duplicate logical Prefab names are rejected");

        var scene = new SceneAggregate("FactoryAndCollision");
        scene.SetInstanceFactory(factory.Build());
        AssertThrows<InvalidOperationException>(
            () => factory.Register(
                new PrefabRef<PrefabProjectile>("late"),
                spawn => new PrefabProjectile(spawn.Position)),
            "Prefab catalog freezes before runtime gameplay");

        var player = scene.Add(new CollisionProbe(
            new Vector2D(10, 10),
            CollisionShape2D.Box(20, 20)));
        scene.Add(new PrefabSpawner(projectilePrefab, new Vector2D(18, 10)));
        scene.PerformStep(.016d);

        PrefabProjectile projectile = scene.FindByType<PrefabProjectile>().Single();
        Assert(projectile.Position == new Vector2D(18, 10) &&
               player.First<PrefabProjectile>() == projectile &&
               player.All<PrefabProjectile>().Count == 1,
            "Typed Prefab Spawn commits at the boundary and participates in collision queries");
        Assert(scene.QueryArea<PrefabProjectile>(new Bounds2D(0, 0, 30, 30)).Count == 1 &&
               scene.QueryRadius<PrefabProjectile>(new Vector2D(10, 10), 10).Count == 1,
            "Area and radius spatial queries filter active colliders by runtime type");
        Assert(CollisionMath2D.Intersects(
                CollisionShape2D.Circle(5),
                Transform2D.Default,
                CollisionShape2D.Box(4, 4),
                Transform2D.Default with { Position = new Vector2D(6, 0) }),
            "Circle/box narrow-phase accepts edge overlap");
        AssertThrows<ArgumentOutOfRangeException>(
            () => CollisionShape2D.Circle(0),
            "Invalid collider dimensions fail during authoring");

        SceneRef? requested = null;
        scene.SetSceneSwitchRequester(next => requested = next);
        var switcher = scene.Add(new SceneSwitchProbe());
        SceneRef nextScene = new("Next");
        switcher.Go(nextScene);
        Assert(requested == nextScene,
            "GameInstance Scene requests remain logical and delegate commit timing to Hosting");

        player.IsPersistent = true;
        scene.Background = BackgroundConfig.FromColor(new Vector4(1, 0, 0, 1));
        scene.Start();
        scene.TransitionTo("Next");
        Assert(scene.SceneName == "Next" && scene.FindById(player.Id) == player &&
               scene.InstanceCount == 1 && scene.Layers.Count == 3 &&
               scene.Background == BackgroundConfig.EngineDefault,
            "Scene transition preserves persistent Instances and resets Scene-local definition state");

        Console.WriteLine("\n14. Scene, Prefab, and collision authoring");
        Console.WriteLine("   [PASS] frozen typed Prefab catalog + boundary Spawn");
        Console.WriteLine("   [PASS] Box/Circle collision + area/radius queries");
        Console.WriteLine("   [PASS] logical Scene request + persistent transition semantics");
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

    private sealed class FakeInputProvider(
        IReadOnlyCollection<InputKey>? down = null,
        IReadOnlyCollection<InputKey>? pressed = null,
        IReadOnlyCollection<InputKey>? released = null) : IInputProvider
    {
        public bool IsKeyDown(InputKey key) => down?.Contains(key) == true;
        public bool WasKeyPressed(InputKey key) => pressed?.Contains(key) == true;
        public bool WasKeyReleased(InputKey key) => released?.Contains(key) == true;
        public Vector2D MousePosition => Vector2D.Zero;
        public float MouseScrollDelta => 0;
        public bool IsMouseButtonDown(GameEngine.Core.Domain.Input.MouseButton button) => false;
    }

    private sealed class GameplayProbe : GameInstance
    {
        public static readonly AlarmId TickAlarm = new("tick");
        private int _steps;

        public bool Pressed { get; private set; }
        public bool Released { get; private set; }
        public int FoundAfterQueue { get; private set; }
        public GameplayChild? FoundOnSecondStep { get; private set; }
        public int AlarmCount { get; private set; }

        public override void OnCreate() => SetAlarm(TickAlarm, .02d);

        public override void OnStep(double deltaTime)
        {
            _steps++;
            if (_steps == 1)
            {
                MoveBy(InputAxis2D() * 10f);
                RotateBy(.5f);
                ScaleBy(new Vector2D(2, 3));
                Pressed = KeyPressed(InputKey.Space);
                Released = KeyReleased(InputKey.E);
                Spawn(new GameplayChild());
                FoundAfterQueue = FindAll<GameplayChild>().Count;
            }
            else if (_steps == 2)
            {
                FoundOnSecondStep = FindFirst<GameplayChild>();
                if (FoundOnSecondStep is not null) Destroy(FoundOnSecondStep);
            }
            else
            {
                DestroySelf();
            }
        }

        public override void OnAlarm(AlarmId alarm)
        {
            if (alarm == TickAlarm) AlarmCount++;
        }

        public void RequestDestroy() => DestroySelf();
    }

    private sealed class GameplayChild : GameInstance
    {
        public bool Created { get; private set; }
        public bool Destroyed { get; private set; }
        public int Steps { get; private set; }

        public override void OnCreate() => Created = true;
        public override void OnStep(double deltaTime) => Steps++;
        public override void OnDestroy() => Destroyed = true;
    }

    private sealed class AlarmProbe : GameInstance
    {
        public static readonly AlarmId TickAlarm = new("paused-tick");
        public int AlarmCount { get; private set; }

        public override void OnCreate() => SetAlarm(TickAlarm, .01d);

        public override void OnAlarm(AlarmId alarm)
        {
            if (alarm == TickAlarm) AlarmCount++;
        }
    }

    private sealed class PrefabSpawner(
        PrefabRef<PrefabProjectile> prefab,
        Vector2D position) : GameInstance
    {
        private bool _spawned;

        public override void OnStep(double deltaTime)
        {
            if (_spawned) return;
            _spawned = true;
            Spawn(prefab, position);
        }
    }

    private sealed class PrefabProjectile : GameInstance
    {
        public PrefabProjectile(Vector2D position)
        {
            Position = position;
            Collider = CollisionShape2D.Circle(4);
        }
    }

    private sealed class CollisionProbe : GameInstance
    {
        public CollisionProbe(Vector2D position, CollisionShape2D collider)
        {
            Position = position;
            Collider = collider;
        }

        public T? First<T>() where T : GameInstance => FirstCollision<T>();
        public IReadOnlyList<T> All<T>() where T : GameInstance => Collisions<T>();
    }

    private sealed class SceneSwitchProbe : GameInstance
    {
        public void Go(SceneRef scene) => SwitchScene(scene);
    }

    private sealed class RecordingSpriteBatch : ISpriteBatch
    {
        public BlendMode BlendMode { get; private set; }
        public (bool Test, bool Write) DepthState { get; private set; }
        public ShaderRef? Shader { get; private set; }
        public MaterialRef? Material { get; private set; }
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
        public void SetMaterial(MaterialRef? material) => Material = material;
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
