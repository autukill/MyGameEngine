namespace Engine.DddTests;

using System.Numerics;
using System.Diagnostics;
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
    private static void Main(string[] args)
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
        VerifyEasingTweenAndMotion();
        VerifyGameplayTimeControl();
        VerifyLifecycleSteadyStateAllocations();
        if (args.Contains("--benchmark-spatial", StringComparer.Ordinal))
            MeasureSpatialQueries();
        if (args.Contains("--benchmark-lifecycle", StringComparer.Ordinal))
            MeasureLifecycleFrames();

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
        var directedPrefab = new PrefabRef<PrefabProjectile, ProjectileArgs>(
            "test.projectile.directed");
        var factory = new InstanceFactory();
        factory.Register(projectilePrefab, spawn => new PrefabProjectile(spawn.Position));
        factory.Register(
            directedPrefab,
            (in ProjectileArgs args) => new PrefabProjectile(args.Position, args.Radius));
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

        var typedScene = new SceneAggregate("TypedPrefabArgs");
        typedScene.SetInstanceFactory(factory);
        var typedArgs = new ProjectileArgs(new Vector2D(40, 50), 7f);
        typedScene.Add(new ParameterizedSpawner(directedPrefab, typedArgs));
        typedScene.PerformStep(.016d);
        PrefabProjectile typedProjectile = typedScene.FindByType<PrefabProjectile>().Single();
        Assert(typedProjectile.Position == typedArgs.Position &&
               typedProjectile.Collider == CollisionShape2D.Circle(typedArgs.Radius),
            "Generic Prefab arguments flow through the typed in-parameter Spawn path");

        SceneRef? requested = null;
        scene.SetSceneSwitchRequester(next => requested = next);
        var switcher = scene.Add(new SceneSwitchProbe());
        SceneRef nextScene = new("Next");
        switcher.Go(nextScene);
        Assert(requested == nextScene,
            "GameInstance Scene requests remain logical and delegate commit timing to Hosting");

        var typedRequester = new RecordingSceneSwitchRequester();
        scene.SetSceneSwitchRequester(typedRequester);
        var resultsScene = new SceneRef<SceneResultsArgs>("Results");
        var resultsArgs = new SceneResultsArgs(123, 4.5d);
        switcher.Go(resultsScene, resultsArgs);
        Assert(typedRequester.Scene == resultsScene.Untyped &&
               typedRequester.Results == resultsArgs,
            "GameInstance preserves typed Scene arguments through the gameplay boundary");

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
        Console.WriteLine("   [PASS] zero-boxing typed Prefab argument path");
        Console.WriteLine("   [PASS] Box/Circle collision + area/radius queries");
        Console.WriteLine("   [PASS] logical Scene request + persistent transition semantics");
    }

    private static void VerifyEasingTweenAndMotion()
    {
        foreach (EasingKind kind in Enum.GetValues<EasingKind>())
        {
            Assert(Nearly(Easing.Evaluate(kind, 0f), 0f) &&
                   Nearly(Easing.Evaluate(kind, 1f), 1f),
                $"{kind} preserves normalized endpoints");
        }

        Assert(Easing.Evaluate(EasingKind.Linear, -1f) == 0f &&
               Easing.Evaluate(EasingKind.Linear, 2f) == 1f,
            "Easing clamps finite progress to the normalized interval");
        Assert(Nearly(Easing.Evaluate(EasingKind.QuadIn, .25f), .0625f) &&
               Nearly(Easing.Evaluate(EasingKind.QuadOut, .25f), .4375f) &&
               Easing.Evaluate(EasingKind.BackOut, .6f) > 1f,
            "Representative ease-in, ease-out, and overshoot curves remain distinct");

        Assert(Nearly(Tween.Progress(.5f, 2f), .25f) &&
               Nearly(Tween.EasedProgress(.5f, 2f, EasingKind.QuadIn), .0625f) &&
               Nearly(Tween.Lerp(10f, 20f, .5f), 15f) &&
               Nearly(Tween.Lerp(10f, 20f, .5d, 2d, EasingKind.QuadIn), 10.625f) &&
               Tween.Lerp(Vector2D.Zero, new Vector2D(10, 20), .25f) ==
                   new Vector2D(2.5f, 5f) &&
               Tween.Lerp(Vector4.Zero, Vector4.One, .5f, EasingKind.QuadIn) ==
                   new Vector4(.25f),
            "Tween separates normalized/eased progress and interpolates scalar, position, and color values");

        float degrees = MathF.PI / 180f;
        Assert(Nearly(Tween.AngleRadians(350f * degrees, 10f * degrees, .5f), MathF.Tau),
            "Angle tween takes the shortest path across the radians wrap boundary");

        Assert(Nearly(Motion.MoveTowards(0f, 10f, 3f), 3f) &&
               Motion.MoveTowards(9f, 10f, 3f) == 10f &&
               Motion.MoveTowards(Vector2D.Zero, new Vector2D(3, 4), 2f) ==
                   new Vector2D(1.2f, 1.6f),
            "MoveTowards caps scalar and vector travel without overshooting");

        float halfStep = Motion.Damp(0f, 10f, .5f, .5f);
        float quarterSteps = Motion.Damp(Motion.Damp(0f, 10f, .5f, .25f), 10f, .5f, .25f);
        Assert(Nearly(halfStep, 5f) && Nearly(quarterSteps, halfStep) &&
               Motion.Damp(0f, 10f, 0f, .1f) == 10f &&
               Motion.Damp(0f, 10f, 0f, 0f) == 0f,
            "Half-life damping is frame-rate independent and has explicit zero-time behavior");
        Assert(Nearly(Motion.DampAngleRadians(
                350f * degrees, 10f * degrees, .5f, .5f), MathF.Tau),
            "Angle damping uses the shortest radians path");

        AssertThrows<ArgumentOutOfRangeException>(
            () => Easing.Evaluate(EasingKind.Linear, float.NaN),
            "Easing rejects non-finite progress");
        AssertThrows<ArgumentOutOfRangeException>(
            () => Tween.Progress(1f, 0f),
            "Tween duration must be positive");
        AssertThrows<ArgumentOutOfRangeException>(
            () => Motion.Damp(0f, 1f, -.1f, .016f),
            "Damping rejects a negative half-life");

        Console.WriteLine("\n15. Easing, Tween, and frame-rate independent Motion");
        Console.WriteLine("   [PASS] normalized curve families + strict inputs");
        Console.WriteLine("   [PASS] scalar/vector/color/shortest-angle interpolation");
        Console.WriteLine("   [PASS] bounded movement + composable half-life damping");
    }

    private static void VerifyGameplayTimeControl()
    {
        var scene = new SceneAggregate("GameplayTime");
        scene.SetSprites(new FakeSpriteResolver());
        int beforeSteps = 0;
        int afterSteps = 0;
        scene.OnBeforeStep = _ => beforeSteps++;
        scene.OnAfterStep = _ => afterSteps++;
        var gameplay = scene.Add(new TimeProbe(InstanceTimeMode.Gameplay));
        var unscaled = scene.Add(new TimeProbe(InstanceTimeMode.Unscaled));

        scene.Time.TimeScale = .5d;
        scene.PerformInput([InputKey.P], []);
        scene.PerformStep(.5d);
        Assert(gameplay.Steps == 1 && Nearly((float)gameplay.LastDelta, .25f) &&
               unscaled.Steps == 1 && Nearly((float)unscaled.LastDelta, .5f),
            "Gameplay time is scaled while Unscaled instances receive real update delta");
        Assert(gameplay.AlarmCount == 0 && unscaled.AlarmCount == 1 &&
               Nearly(gameplay.ImageIndex, 1f) && Nearly(unscaled.ImageIndex, 2f),
            "Alarm and Sprite animation advance in each Instance's selected time domain");
        Assert(gameplay.KeyEvents == 1 && unscaled.KeyEvents == 1 &&
               beforeSteps == 1 && afterSteps == 1,
            "Running gameplay dispatches input edges and Scene gameplay hooks normally");

        GameplayPauseKey focus = new("test.focus");
        GameplayPauseKey playerPause = new("test.player");
        scene.Time.Pause(focus);
        scene.Time.Pause(playerPause);
        scene.Time.Pause(playerPause);
        Assert(scene.Time.IsPaused && scene.Time.PauseRequestCount == 2,
            "External pause keys are independent and duplicate requests are idempotent");

        scene.PerformInput([InputKey.P], []);
        scene.PerformStep(.5d);
        var batch = new RecordingSpriteBatch();
        scene.DrawActive(batch);
        Assert(gameplay.Steps == 1 && gameplay.AlarmCount == 0 &&
               Nearly(gameplay.ImageIndex, 1f) && gameplay.KeyEvents == 1,
            "Paused Gameplay skips Step, Alarm, animation, and input edges entirely");
        Assert(unscaled.Steps == 2 && unscaled.AlarmCount == 1 &&
               Nearly(unscaled.ImageIndex, 0f) && unscaled.KeyEvents == 2,
            "Unscaled instances continue Step, animation loops, and input while paused");
        Assert(gameplay.Draws == 1 && unscaled.Draws == 1 &&
               beforeSteps == 1 && afterSteps == 1,
            "Draw continues while paused and Scene gameplay hooks remain frozen");

        scene.Time.Resume(focus);
        Assert(scene.Time.IsPaused, "Releasing one external owner does not resume another");
        scene.Time.Resume(playerPause);
        scene.PerformStep(.5d);
        Assert(!scene.Time.IsPaused && gameplay.Steps == 2 && gameplay.AlarmCount == 1 &&
               scene.Time.Current == new GameplayTimeSnapshot(.5d, .25d, .5d, false),
            "Last pause release resumes scaled gameplay with an explicit time snapshot");

        GameplayPauseKey owned = new("test.instance-owner");
        var firstOwner = scene.Add(new TimeProbe(InstanceTimeMode.Unscaled));
        var secondOwner = scene.Add(new TimeProbe(InstanceTimeMode.Unscaled));
        firstOwner.HoldPause(owned);
        secondOwner.HoldPause(owned);
        firstOwner.ReleasePause(owned);
        Assert(scene.Time.IsPaused && scene.Time.PauseRequestCount == 1,
            "Instance pause ownership distinguishes identical keys from different owners");
        secondOwner.SetActive(false, scene.RaiseEvent);
        Assert(!scene.Time.IsPaused && scene.Time.PauseRequestCount == 0,
            "Deactivating an owner automatically releases its pause requests");
        firstOwner.HoldPause(owned);
        scene.Destroy(firstOwner.Id);
        Assert(!scene.Time.IsPaused,
            "Destroying an owner automatically releases its pause requests");

        scene.Time.Pause(focus);
        unscaled.HoldPause(owned);
        scene.Start();
        scene.TransitionTo("GameplayTime.Next");
        Assert(scene.Time.IsPaused && scene.Time.PauseRequestCount == 1 &&
               scene.Time.TimeScale == 1d,
            "Scene transitions clear Scene-owned time state but retain external pause reasons");
        scene.Time.Resume(focus);

        AssertThrows<ArgumentOutOfRangeException>(
            () => scene.Time.TimeScale = 0d,
            "Time scale zero is not an alias for pause");
        AssertThrows<ArgumentOutOfRangeException>(
            () => scene.Time.TimeScale = 8.1d,
            "Time scale rejects values above the supported range");

        Console.WriteLine("\n16. Gameplay pause and time domains");
        Console.WriteLine("   [PASS] scaled Gameplay + real-time Unscaled scheduling");
        Console.WriteLine("   [PASS] paused Step/Alarm/animation/input + continuing Draw");
        Console.WriteLine("   [PASS] owner-aware pause cleanup + Scene/Host scope boundary");
    }

    private static void MeasureSpatialQueries()
    {
        Console.WriteLine("\n18. Spatial query benchmark (linear scan)");
        foreach (int count in new[] { 100, 1_000, 10_000 })
        {
            var scene = new SceneAggregate($"Spatial-{count}");
            for (int i = 0; i < count; i++)
            {
                scene.Add(new SpatialProbe(new Vector2D(
                    (i % 100) * 20f,
                    (i / 100) * 20f)));
            }

            const int queries = 500;
            // Warm through tiered JIT/PGO before timing so different population sizes compare fairly.
            for (int i = 0; i < 500; i++)
                _ = scene.QueryRadius<SpatialProbe>(new Vector2D(1_000, 1_000), 6f);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int hits = 0;
            for (int i = 0; i < queries; i++)
            {
                Vector2D center = new((i % 100) * 20f, ((i * 17) % count / 100) * 20f);
                hits += scene.QueryRadius<SpatialProbe>(center, 6f).Count;
            }
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                $"   {count,6:N0} colliders: {elapsedMs / queries,8:F4} ms/query, " +
                $"{allocated / queries,6:N0} B/query, hits={hits}");
        }
    }

    private static void VerifyLifecycleSteadyStateAllocations()
    {
        var scene = new SceneAggregate("AllocationFreeLifecycle");
        var batch = new RecordingSpriteBatch();
        var pressed = new[] { InputKey.Space };
        var released = new[] { InputKey.Space };

        const int instanceCount = 128;
        for (int i = 0; i < instanceCount; i++)
            scene.Add(new AllocationFreeProbe(new LayerDepth(i % 4)));

        scene.MarkEventsAsCommitted();
        for (int i = 0; i < 64; i++)
            RunLifecycleFrame(scene, batch, pressed, released);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            scene.PerformInput(pressed, released);
        long inputAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            scene.PerformStep(1d / 60d);
        long stepAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            scene.DrawActive(batch);
        long drawAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            scene.DrawGUI(batch);
        long guiAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long allocated = inputAllocated + stepAllocated + drawAllocated + guiAllocated;
        Assert(allocated == 0,
            $"Scene lifecycle remains allocation-free after warm-up " +
            $"(Input={inputAllocated:N0}, Step={stepAllocated:N0}, " +
            $"Draw={drawAllocated:N0}, GUI={guiAllocated:N0} B)");

        var mutationScene = new SceneAggregate("PhaseVisibility");
        var addedDuringBegin = new PhaseProbe();
        var removedDuringBegin = mutationScene.Add(new PhaseProbe());
        mutationScene.Add(new BeginStepMutationProbe(
            mutationScene,
            addedDuringBegin,
            removedDuringBegin.Id));
        mutationScene.PerformStep(.016d);
        Assert(addedDuringBegin.Steps == 1 && removedDuringBegin.Steps == 0,
            "Direct Begin Step mutations retain the existing same-frame Step visibility boundary");

        var drawScene = new SceneAggregate("StableDrawOrder");
        var order = new List<string>();
        drawScene.Add(new DrawOrderProbe("back", 20, order));
        drawScene.Add(new DrawOrderProbe("equal-first", 10, order));
        drawScene.Add(new DrawOrderProbe("equal-second", 10, order));
        drawScene.Add(new DrawOrderProbe("front", -10, order));
        drawScene.DrawActive(batch);
        Assert(order.SequenceEqual(["back", "equal-first", "equal-second", "front"]),
            "Draw keeps descending depth and stable insertion order for equal depths");

        Console.WriteLine("\n17. Scene lifecycle steady-state allocations");
        Console.WriteLine("   [PASS] Input + Step + Draw + DrawGUI remain at 0 B/frame after warm-up");
        Console.WriteLine("   [PASS] phase mutation visibility and stable depth ordering are preserved");
    }

    private static void RunLifecycleFrame(
        SceneAggregate scene,
        ISpriteBatch batch,
        IReadOnlyList<InputKey> pressed,
        IReadOnlyList<InputKey> released)
    {
        scene.PerformInput(pressed, released);
        scene.PerformStep(1d / 60d);
        scene.DrawActive(batch);
        scene.DrawGUI(batch);
    }

    private static void MeasureLifecycleFrames()
    {
        Console.WriteLine("\n19. Scene lifecycle benchmark");
        foreach (int count in new[] { 100, 1_000, 10_000 })
        {
            var scene = new SceneAggregate($"Lifecycle-{count}");
            var batch = new RecordingSpriteBatch();
            for (int i = 0; i < count; i++)
                scene.Add(new AllocationFreeProbe(new LayerDepth(i % 8)));
            scene.MarkEventsAsCommitted();

            for (int i = 0; i < 64; i++)
                RunLifecycleFrame(scene, batch, Array.Empty<InputKey>(), Array.Empty<InputKey>());

            const int frames = 240;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < frames; i++)
                RunLifecycleFrame(scene, batch, Array.Empty<InputKey>(), Array.Empty<InputKey>());
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                $"   {count,6:N0} instances: {elapsedMs / frames,8:F4} ms/frame, " +
                $"{allocated / frames,6:N0} B/frame");
        }
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

    private static bool Nearly(float actual, float expected, float tolerance = .00001f) =>
        MathF.Abs(actual - expected) <= tolerance;

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
        public PrefabProjectile(Vector2D position, float radius = 4f)
        {
            Position = position;
            Collider = CollisionShape2D.Circle(radius);
        }
    }

    private readonly record struct ProjectileArgs(Vector2D Position, float Radius);

    private sealed class ParameterizedSpawner(
        PrefabRef<PrefabProjectile, ProjectileArgs> prefab,
        ProjectileArgs args) : GameInstance
    {
        private bool _spawned;

        public override void OnStep(double deltaTime)
        {
            if (_spawned) return;
            _spawned = true;
            Spawn(prefab, args);
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

        public void Go<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct =>
            SwitchScene(scene, args);
    }

    private readonly record struct SceneResultsArgs(int Score, double ElapsedSeconds);

    private sealed class RecordingSceneSwitchRequester : ISceneSwitchRequester
    {
        public SceneRef Scene { get; private set; }
        public SceneResultsArgs Results { get; private set; }

        public void Request(SceneRef scene) => Scene = scene;

        public void Request<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct
        {
            Scene = scene.Untyped;
            if (args is SceneResultsArgs results)
                Results = results;
        }
    }

    private sealed class SpatialProbe : GameInstance
    {
        public SpatialProbe(Vector2D position)
        {
            Position = position;
            Collider = CollisionShape2D.Circle(4f);
        }
    }

    private sealed class TimeProbe : GameInstance
    {
        private static readonly AlarmId TickAlarm = new("time-probe.tick");

        public int Steps { get; private set; }
        public int AlarmCount { get; private set; }
        public int KeyEvents { get; private set; }
        public int Draws { get; private set; }
        public double LastDelta { get; private set; }

        public TimeProbe(InstanceTimeMode timeMode)
        {
            TimeMode = timeMode;
            Sprite = new SpriteRef("time-probe");
        }

        public override void OnCreate() => SetAlarm(TickAlarm, .4d);

        public override void OnStep(double deltaTime)
        {
            Steps++;
            LastDelta = deltaTime;
        }

        public override void OnAlarm(AlarmId alarm)
        {
            if (alarm == TickAlarm) AlarmCount++;
        }

        public override void OnKeyDown(InputKey key) => KeyEvents++;

        public override void OnDraw(ISpriteBatch batch)
        {
            Draws++;
            base.OnDraw(batch);
        }

        public void HoldPause(GameplayPauseKey key) => PauseGameplay(key);
        public void ReleasePause(GameplayPauseKey key) => ResumeGameplay(key);
    }

    private sealed class AllocationFreeProbe : GameInstance
    {
        public int CallbackCount { get; private set; }

        public AllocationFreeProbe(LayerDepth depth)
            : base(nameof(AllocationFreeProbe), Vector2D.Zero, depth)
        {
        }

        public override void OnKeyDown(InputKey key) => CallbackCount++;
        public override void OnKeyUp(InputKey key) => CallbackCount++;
        public override void OnBeginStep(double deltaTime) => CallbackCount++;
        public override void OnStep(double deltaTime) => CallbackCount++;
        public override void OnEndStep(double deltaTime) => CallbackCount++;
        public override void OnBeginDraw(ISpriteBatch batch) => CallbackCount++;
        public override void OnDraw(ISpriteBatch batch) => CallbackCount++;
        public override void OnEndDraw(ISpriteBatch batch) => CallbackCount++;
        public override void OnDrawGUI(ISpriteBatch batch) => CallbackCount++;
    }

    private sealed class PhaseProbe : GameInstance
    {
        public int Steps { get; private set; }
        public override void OnStep(double deltaTime) => Steps++;
    }

    private sealed class BeginStepMutationProbe(
        SceneAggregate scene,
        GameInstance instanceToAdd,
        InstanceId instanceToDestroy) : GameInstance
    {
        private bool _mutated;

        public override void OnBeginStep(double deltaTime)
        {
            if (_mutated) return;
            _mutated = true;
            scene.Add(instanceToAdd);
            scene.Destroy(instanceToDestroy);
        }
    }

    private sealed class DrawOrderProbe : GameInstance
    {
        private readonly string _name;
        private readonly List<string> _order;

        public DrawOrderProbe(string name, int depth, List<string> order)
            : base(nameof(DrawOrderProbe), Vector2D.Zero, new LayerDepth(depth))
        {
            _name = name;
            _order = order;
        }

        public override void OnDraw(ISpriteBatch batch) => _order.Add(_name);
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
