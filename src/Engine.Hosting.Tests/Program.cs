namespace GameEngine.Hosting.Tests;

using GameEngine.Core.Domain.Events;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Core.Infrastructure.Diagnostics;
using GameEngine.Core.Infrastructure.Graphics;
using GameEngine.Features.Bloom.Domain;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using SkiaSharp;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Engine Hosting Smoke Test ===\n");
        TestBuilderPlans();
        TestBuilderValidation();
        TestLogicalInputMap();
        TestGameplayCooldown();
        TestGameplayTags();
        TestGameplayBehaviors();
        TestSceneCatalogAndPrefabs();
        TestResourceOwnership();
        TestDefaultPresentationControllers();
        TestPerformanceTelemetry();
        TestContentHotReloadOptions();
        TestContentHotReloadCoordinator();
        TestShaderHotReloadConfiguration();
        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Engine Hosting smoke tests passed ==="
            : $"=== {_failures} Engine Hosting test(s) FAILED ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void TestBuilderPlans()
    {
        Console.WriteLine("1. Immutable application plans");
        var bloom = new BloomSettings(0.4f, 1.4f, 1f, 2, BloomResolution.Half);
        var tone = new ToneMappingSettings(ToneMappingOperator.Aces, 0.5f, 2.2f);
        var package = new ContentPackageRef("game.assets", "game/assets.json");
        var plan = GameApplication.Create(new EngineWindowOptions(Title: "Hosting Test"))
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(package)
                .UseHdr(tone, bloom)
                .EnableStencilMasking())
            .ConfigureScene("Main", _ => { })
            .BuildPlan();

        Check(plan.WindowOptions.Title == "Hosting Test" &&
              plan.SceneName == "Main" &&
              plan.Renderer.ContentPackagesRoot == "AssetsCompiled" &&
              plan.Renderer.ContentManifest == "game/assets.json" &&
              plan.Renderer.ContentPackage == package &&
              plan.Renderer.HdrEnabled &&
              plan.Renderer.Bloom == bloom &&
              plan.Renderer.ToneMapping == tone &&
              plan.Renderer.StencilMaskingEnabled &&
              plan.Renderer.SceneGuiEnabled &&
              plan.Renderer.ResolvedViewports.Single().Slot == ViewportSlotRef.Main,
            "Builder freezes window, content, HDR, Bloom, Stencil, and Scene configuration");

        var ldr = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.DisableSceneGui())
            .ConfigureScene("Ldr", _ => { })
            .BuildPlan();
        Check(!ldr.Renderer.HdrEnabled &&
              ldr.Renderer.Bloom is null &&
              !ldr.Renderer.SceneGuiEnabled,
            "Default renderer remains LDR and optional features are lazy");

        var multiViewport = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseSingleCameraViewports(views => views
                .Add("left", ViewportRect.LeftHalf, ViewportFitMode.Cover)
                .Add("right", ViewportRect.RightHalf, ViewportFitMode.Contain)))
            .ConfigureScene("MultiViewport", _ => { })
            .BuildPlan();
        Check(multiViewport.Renderer.ResolvedViewports.Count == 2 &&
              multiViewport.Renderer.ResolvedViewports[0].Layer == 0 &&
              multiViewport.Renderer.ResolvedViewports[1].Layer == 1 &&
              multiViewport.Renderer.ResolvedViewports[1].Fit == ViewportFitMode.Contain,
            "Declarative Viewports preserve order and receive stable default layers");

        var follow = new CameraFollowSettings(
            anchor: new System.Numerics.Vector2(0.4f, 0.5f),
            deadZoneSize: new System.Numerics.Vector2(160, 90),
            halfLifeSeconds: 0.15f);
        var renderViews = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseRenderViews(views => views
                .ConfigureMain(ViewportRect.LeftHalf, cameraFollow: follow)
                .Add(
                    "player.two",
                    ViewportRect.RightHalf,
                    renderScale: 0.5f,
                    sceneLayers: SceneLayerFilter.Exclude("MainOnly"),
                    effects: RenderViewEffects.Hdr(ToneMappingSettings.Default),
                    cameraFollow: CameraFollowSettings.Default)))
            .ConfigureScene("Split", _ => { })
            .BuildPlan();
        Check(renderViews.Renderer.MultipleRenderViewsEnabled &&
              renderViews.Renderer.RenderViews is { Count: 2 } definitions &&
              definitions[0].Ref == RenderViewRef.Main &&
              definitions[1].Ref == new RenderViewRef("player.two") &&
              definitions[1].RenderScale == 0.5f &&
              definitions[0].SceneLayers.IsAll &&
              definitions[0].Effects == RenderViewEffects.Direct &&
              definitions[0].CameraFollow == follow &&
              definitions[1].CameraFollow == CameraFollowSettings.Default &&
              definitions[1].SceneLayers.IsExclusive &&
              !definitions[1].SceneLayers.Allows("MainOnly") &&
              definitions[1].SceneLayers.Allows(SceneAggregate.LayerNameInstances) &&
              definitions[1].Effects.IsHdr &&
              definitions[1].Effects.Bloom is null &&
              definitions[1].Effects.AdditionalPassCount == 1 &&
              definitions[1].Effects.AdditionalRenderTargetCount == 1 &&
              renderViews.Renderer.AnyHdrEnabled,
            "Render View plans freeze Camera, layers, and explicit post-processing cost");
        Check(RenderViewLayoutBuilder.ResolveRenderSize(
                  ViewportRect.RightHalf, 0.5f, 801, 601) == (200, 301),
            "Render View size combines shared-edge Viewport rounding and RenderScale deterministically");
    }

    private static void TestBuilderValidation()
    {
        Console.WriteLine("2. Fail-fast configuration validation");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .ConfigureScene("Main", _ => { })
                .BuildPlan(),
            "Missing renderer is rejected before creating a window");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .BuildPlan(),
            "Missing initial Scene is rejected before creating a window");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .UseDefault2DRenderer(),
            "Duplicate default renderer registration is rejected");
        CheckThrows<ArgumentException>(
            () => new Default2DRendererOptions().UseContent(" "),
            "Empty content root is rejected");
        CheckThrows<ArgumentException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .ConfigureScene(" ", _ => { }),
            "Empty Scene name is rejected");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions().UseSingleCameraViewports(_ => { }),
            "An empty Viewport layout is rejected");
        CheckThrows<ArgumentException>(
            () => new Default2DRendererOptions().UseSingleCameraViewports(views => views
                .Add("same", ViewportRect.LeftHalf)
                .Add("same", ViewportRect.RightHalf)),
            "Duplicate Viewport slot names are rejected");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions().UseRenderViews(_ => { }),
            "Multiple Render Views require an explicit secondary View");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new Default2DRendererOptions().UseRenderViews(views => views
                .Add("small", ViewportRect.RightHalf, renderScale: 0f)),
            "Render View scale must remain in the supported range");
        CheckThrows<ArgumentException>(
            () => SceneLayerFilter.Include("Actors", "Actors"),
            "Duplicate Scene layer selections are rejected during configuration");
        Check(RenderViewEffects.Direct.AdditionalPassCount == 0 &&
              RenderViewEffects.Direct.AdditionalRenderTargetCount == 0 &&
              RenderViewEffects.Hdr(
                  ToneMappingSettings.Default,
                  BloomSettings.Default).AdditionalPassCount == 2 &&
              RenderViewEffects.Hdr(
                  ToneMappingSettings.Default,
                  BloomSettings.Default).AdditionalRenderTargetCount == 4,
            "Render View effect profiles expose their exact additional Pass and target cost");
        CheckThrows<ArgumentOutOfRangeException>(
            () => RenderViewEffects.Hdr(default),
            "Default-initialized invalid effect settings are rejected at configuration time");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .UseSingleCameraViewports(views => views.Add("main", ViewportRect.FullScreen))
                .UseRenderViews(views => views.Add("second", ViewportRect.RightHalf)),
            "Mirrored Viewports and independent Render Views cannot be combined");
        var primaryEffects = new Default2DRendererOptions()
            .UseHdr(ToneMappingSettings.Default, BloomSettings.Default)
            .EnableStencilMasking()
            .UseRenderViews(views => views.Add("second", ViewportRect.RightHalf))
            .ToPlan();
        primaryEffects.Validate();
        Check(primaryEffects.MultipleRenderViewsEnabled && primaryEffects.HdrEnabled &&
              primaryEffects.StencilMaskingEnabled &&
              primaryEffects.RenderViews!.All(view => view.CameraFollow is null),
            "Primary HDR/Bloom/Stencil can coexist with lazy secondary LDR Views");
    }

    private static void TestLogicalInputMap()
    {
        Console.WriteLine("3. Logical input actions and axes");
        var fire = new InputActionRef("player.fire");
        var move = new InputAxis2DRef("player.move");
        InputKey[] fireKeys = [InputKey.Space, InputKey.Control];
        InputMap map = new InputMapBuilder()
            .BindAction(fire, fireKeys)
            .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
            .BindAxis2D(move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
            .Build();
        fireKeys[0] = InputKey.Escape;

        var input = new MappedInputProbe();
        input.Down.Add(InputKey.Space);
        input.Pressed.Add(InputKey.Control);
        input.Released.Add(InputKey.Space);
        Check(map.ActionDown(input, fire) &&
              map.ActionPressed(input, fire) &&
              map.ActionReleased(input, fire),
            "Action bindings use OR semantics and freeze caller-owned key arrays");

        input.Down.Clear();
        input.Down.Add(InputKey.D);
        input.Down.Add(InputKey.Right);
        input.Down.Add(InputKey.W);
        Check(map.Axis2D(input, move) == new Vector2D(1, -1),
            "Multiple digital axis schemes combine and clamp to [-1,1]");
        input.Down.Add(InputKey.Left);
        input.Down.Add(InputKey.A);
        Check(map.Axis2D(input, move) == new Vector2D(0, -1),
            "Opposing keys across schemes cancel deterministically");
        input.Down.Add(InputKey.Space);

        var gameplayProbe = new LogicalInputProbe(fire, move);
        Check(!gameplayProbe.FireDown && gameplayProbe.Move == Vector2D.Zero,
            "Instances outside a Scene use an inert logical input Null Object");
        var scene = new SceneAggregate("MappedInput");
        scene.SetInput(input);
        scene.SetInputMap(map);
        scene.Add(gameplayProbe);
        Check(ReferenceEquals(gameplayProbe.MappedInput, map) &&
              gameplayProbe.FireDown && gameplayProbe.Move == new Vector2D(0, -1),
            "Scene injects one immutable map into GameInstance convenience queries");

        var plan = GameApplication.Create()
            .ConfigureInput(bindings => bindings
                .BindAction(fire, InputKey.Space)
                .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S))
            .UseDefault2DRenderer()
            .ConfigureScene("InputPlan", _ => { })
            .BuildPlan();
        Check(plan.InputMap.ActionCount == 1 && plan.InputMap.Axis2DCount == 1,
            "Application plan freezes logical input bindings before window creation");

        CheckThrows<KeyNotFoundException>(
            () => map.ActionDown(input, new InputActionRef("unknown")),
            "Configured maps reject unknown logical actions");
        CheckThrows<ArgumentException>(
            () => new InputMapBuilder()
                .BindAction(new InputActionRef("same"), InputKey.Space)
                .BindAxis2D(
                    new InputAxis2DRef("same"),
                    InputKey.A,
                    InputKey.D,
                    InputKey.W,
                    InputKey.S),
            "One logical name cannot change control kind");
        CheckThrows<ArgumentException>(
            () => new InputMapBuilder().BindAction(
                fire, InputKey.Space, InputKey.Space),
            "Duplicate physical bindings fail during composition");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create().ConfigureInput(_ => { }),
            "An explicitly configured empty input map is rejected");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .ConfigureInput(bindings => bindings.BindAction(fire, InputKey.Space))
                .ConfigureInput(bindings => bindings.BindAction(fire, InputKey.Enter)),
            "Input bindings cannot be configured twice");

        for (int i = 0; i < 64; i++)
        {
            _ = map.ActionDown(input, fire);
            _ = map.ActionPressed(input, fire);
            _ = map.ActionReleased(input, fire);
            _ = map.Axis2D(input, move);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            _ = map.ActionDown(input, fire);
            _ = map.ActionPressed(input, fire);
            _ = map.ActionReleased(input, fire);
            _ = map.Axis2D(input, move);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Logical action and axis queries remain allocation-free ({allocated:N0} B)");

        input.Pressed.Clear();
        var bufferedProbe = new BufferedInputProbe(fire, 0.1d);
        scene.Add(bufferedProbe);
        input.Pressed.Add(InputKey.Space);
        scene.PerformStep(0.01d);
        input.Pressed.Clear();
        scene.PerformStep(0.05d);
        Check(bufferedProbe.Buffer.IsBuffered && bufferedProbe.Buffer.TryConsume() &&
              !bufferedProbe.Buffer.TryConsume(),
            "GameInstance captures a press until one explicit buffered consumption");

        input.Pressed.Add(InputKey.Space);
        scene.PerformStep(0.01d);
        input.Pressed.Clear();
        var pause = new GameplayPauseKey("input-buffer-test");
        scene.Time.Pause(pause);
        scene.PerformStep(1d);
        Check(bufferedProbe.Buffer.IsBuffered,
            "Gameplay-time input buffers freeze while the Scene is paused");
        scene.Time.Resume(pause);
        scene.PerformStep(0.11d);
        Check(!bufferedProbe.Buffer.IsBuffered,
            "Buffered presses expire in the owning Instance time domain");

        var grace = new GameplayGracePeriod(0.1d);
        grace.Update(condition: true, deltaTime: 0.02d);
        grace.Update(condition: false, deltaTime: 0.06d);
        Check(grace.IsOpen && Math.Abs(grace.RemainingSeconds - 0.04d) < 0.000001d,
            "Grace periods retain a recently true gameplay condition");
        grace.Update(condition: false, deltaTime: 0.05d);
        Check(!grace.IsOpen,
            "Grace periods close deterministically after their duration");

        CheckThrows<ArgumentOutOfRangeException>(
            () => new InputActionBuffer(fire, 0d),
            "Input buffers reject non-positive windows");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new GameplayGracePeriod(double.NaN),
            "Grace periods reject non-finite durations");

        var allocationBuffer = new InputActionBuffer(fire, 0.1d);
        var allocationGrace = new GameplayGracePeriod(0.1d);
        for (int i = 0; i < 64; i++)
        {
            allocationBuffer.Update(i % 8 == 0, 1d / 60d);
            allocationGrace.Update(i % 8 == 0, 1d / 60d);
            if (i % 16 == 0) allocationBuffer.TryConsume();
        }
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            allocationBuffer.Update(i % 8 == 0, 1d / 60d);
            allocationGrace.Update(i % 8 == 0, 1d / 60d);
            if (i % 16 == 0) allocationBuffer.TryConsume();
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Input buffer and grace-period updates remain allocation-free ({allocated:N0} B)");
    }

    private static void TestGameplayTags()
    {
        Console.WriteLine("4. Strongly typed Gameplay Tags");
        var enemy = new GameplayTag("actor.enemy");
        var damageable = new GameplayTag("combat.damageable");
        var player = new GameplayTag("actor.player");

        var source = new TaggedProbe(new Vector2D(0f, 0f), player);
        var nearEnemy = new TaggedProbe(new Vector2D(1f, 0f), enemy, damageable);
        var farEnemy = new TaggedProbe(new Vector2D(100f, 0f), enemy);
        var friendly = new TaggedProbe(new Vector2D(0f, 1f), player);
        var inactiveEnemy = new TaggedProbe(new Vector2D(0f, -1f), enemy);
        inactiveEnemy.SetActive(false, _ => { });

        var scene = new SceneAggregate("GameplayTags");
        scene.Add(source);
        scene.Add(nearEnemy);
        scene.Add(farEnemy);
        scene.Add(friendly);
        scene.Add(inactiveEnemy);

        Check(nearEnemy.TagCount == 2 && nearEnemy.HasTag(enemy) &&
              !nearEnemy.AddTag(enemy) && nearEnemy.RemoveTag(damageable) &&
              !nearEnemy.RemoveTag(damageable) && nearEnemy.AddTag(damageable),
            "Tag membership is validated, idempotent, and mutable at runtime");
        Check(enemy != new GameplayTag("Actor.Enemy"),
            "Gameplay tag names remain case-sensitive");

        Check(ReferenceEquals(scene.FindFirst<TaggedProbe>(enemy), nearEnemy) &&
              scene.FindAll<TaggedProbe>(enemy).Count == 3 &&
              scene.CountInstances<TaggedProbe>(enemy) == 3,
            "Find and count combine runtime type with one required tag");
        Check(ReferenceEquals(scene.FirstCollision<TaggedProbe>(source, enemy), nearEnemy) &&
              scene.Collisions<TaggedProbe>(source, enemy).Count == 1,
            "Tag-filtered collisions exclude inactive and non-matching instances");
        Check(scene.QueryArea<TaggedProbe>(new Bounds2D(-4f, -4f, 4f, 4f), enemy).Count == 1 &&
              scene.QueryRadius<TaggedProbe>(Vector2D.Zero, 4f, enemy).Count == 1,
            "Area and radius queries preserve active Collider filtering with tags");

        friendly.AddTag(enemy);
        Check(scene.Collisions<TaggedProbe>(source, enemy).Count == 2,
            "Runtime tag additions affect subsequent queries immediately");
        friendly.RemoveTag(enemy);

        CheckThrows<ArgumentException>(
            () => source.AddTag(default),
            "Instances reject default gameplay tags");
        CheckThrows<ArgumentException>(
            () => scene.FindFirst<GameInstance>(default),
            "Scene queries reject default gameplay tags");
        CheckThrows<ArgumentException>(
            () => new GameplayTag(" "),
            "Gameplay tag names cannot be blank");

        var results = new GameplayQueryBuffer<TaggedProbe>(4);
        var bounds = new Bounds2D(-4f, -4f, 4f, 4f);
        for (int i = 0; i < 64; i++)
        {
            scene.FindAll(enemy, results);
            scene.Collisions(source, enemy, results);
            scene.QueryArea(bounds, enemy, results);
            scene.QueryRadius(Vector2D.Zero, 4f, enemy, results);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            scene.FindAll(enemy, results);
            scene.Collisions(source, enemy, results);
            scene.QueryArea(bounds, enemy, results);
            scene.QueryRadius(Vector2D.Zero, 4f, enemy, results);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Buffered tag queries remain allocation-free after warmup ({allocated:N0} B)");

        scene.Destroy(nearEnemy.Id);
        Check(scene.CountInstances<TaggedProbe>(enemy) == 2,
            "Destroyed tagged instances disappear from later queries");
    }

    private static void TestGameplayCooldown()
    {
        Console.WriteLine("3b. Gameplay cooldown");
        var cooldown = new GameplayCooldown(0.5d);
        Check(cooldown.IsReady && cooldown.RemainingSeconds == 0d && cooldown.Progress == 1d,
            "Cooldown starts ready");
        Check(cooldown.TryUse() && !cooldown.TryUse() && !cooldown.IsReady &&
              cooldown.RemainingSeconds == 0.5d && cooldown.Progress == 0d,
            "TryUse atomically starts a ready cooldown");

        cooldown.Update(0.2d);
        Check(!cooldown.IsReady && Math.Abs(cooldown.RemainingSeconds - 0.3d) < 0.000001d &&
              Math.Abs(cooldown.Progress - 0.4d) < 0.000001d,
            "Update exposes normalized recovery progress");
        cooldown.Update(1d);
        Check(cooldown.IsReady && cooldown.RemainingSeconds == 0d && cooldown.Progress == 1d,
            "Cooldown clamps to ready at its boundary");

        cooldown.Restart();
        cooldown.Update(0.1d);
        cooldown.Restart();
        Check(cooldown.RemainingSeconds == cooldown.DurationSeconds,
            "Restart restores the full duration from any state");
        cooldown.Reset();
        Check(cooldown.IsReady && cooldown.RemainingSeconds == 0d,
            "Reset makes the cooldown immediately ready");

        var noCooldown = new GameplayCooldown(0d);
        Check(noCooldown.TryUse() && noCooldown.TryUse() && noCooldown.IsReady &&
              noCooldown.Progress == 1d,
            "Zero duration explicitly means no cooldown");

        CheckThrows<ArgumentOutOfRangeException>(
            () => new GameplayCooldown(double.NaN),
            "Cooldown rejects non-finite durations");
        CheckThrows<ArgumentOutOfRangeException>(
            () => cooldown.Update(-0.01d),
            "Cooldown rejects negative delta time");

        var gameplayOwner = new CooldownProbe(1d);
        var unscaledOwner = new CooldownProbe(1d) { TimeMode = InstanceTimeMode.Unscaled };
        var scene = new SceneAggregate("CooldownTimeDomains");
        scene.Add(gameplayOwner);
        scene.Add(unscaledOwner);
        gameplayOwner.Cooldown.TryUse();
        unscaledOwner.Cooldown.TryUse();
        var pause = new GameplayPauseKey("cooldown-test");
        scene.Time.Pause(pause);
        scene.PerformStep(0.25d);
        Check(gameplayOwner.Cooldown.RemainingSeconds == 1d &&
              unscaledOwner.Cooldown.RemainingSeconds == 0.75d,
            "Owner-driven updates inherit Gameplay pause and Unscaled time semantics");
        scene.Time.Resume(pause);
        gameplayOwner.SetActive(false, _ => { });
        scene.PerformStep(0.25d);
        Check(gameplayOwner.Cooldown.RemainingSeconds == 1d &&
              unscaledOwner.Cooldown.RemainingSeconds == 0.5d,
            "Inactive owners do not advance their cooldowns");

        var allocationCooldown = new GameplayCooldown(0.05d);
        for (int i = 0; i < 64; i++)
        {
            allocationCooldown.Update(1d / 60d);
            allocationCooldown.TryUse();
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            allocationCooldown.Update(1d / 60d);
            allocationCooldown.TryUse();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Cooldown updates remain allocation-free after warmup ({allocated:N0} B)");
    }

    private static void TestGameplayBehaviors()
    {
        Console.WriteLine("5. Lightweight Gameplay Behavior composition");
        var order = new List<string>();
        var owner = new BehaviorOwnerProbe(order);
        var first = owner.UseBehavior(new RecordingBehavior<BehaviorOwnerProbe>("first", order));
        owner.UseBehavior(new RecordingBehavior<BehaviorOwnerProbe>("second", order));
        var scene = new SceneAggregate("Behaviors");
        scene.Add(owner);
        scene.PerformStep(0.25d);

        string[] expected =
        [
            "owner.create", "first.create", "second.create",
            "owner.begin", "first.begin", "second.begin",
            "owner.step", "first.step", "second.step",
            "owner.end", "first.end", "second.end"
        ];
        Check(order.SequenceEqual(expected) && owner.BehaviorCount == 2 &&
              ReferenceEquals(first.Owner, owner) &&
              ReferenceEquals(owner.FindBehavior<RecordingBehavior<BehaviorOwnerProbe>>(), first),
            "Owner hooks run first and Behaviors run in declaration order");

        CheckThrows<InvalidOperationException>(
            () => owner.UseBehavior(new CountingBehavior<BehaviorOwnerProbe>()),
            "Behavior composition freezes when an Instance enters a Scene");
        var unattachedOwner = new BehaviorOwnerProbe([]);
        CheckThrows<InvalidOperationException>(
            () => unattachedOwner.UseBehavior(first),
            "One Behavior instance cannot be shared by multiple owners");
        CheckThrows<ArgumentException>(
            () => unattachedOwner.UseBehavior(new CountingBehavior<HostingPrefabProbe>()),
            "Strongly typed Behaviors reject incompatible owners during composition");

        order.Clear();
        scene.Destroy(owner.Id);
        Check(order.SequenceEqual(
            new[] { "owner.destroy", "second.destroy", "first.destroy" }),
            "Owner destroys first and Behaviors unwind in reverse declaration order");

        var failureOrder = new List<string>();
        var failingOwner = new BehaviorOwnerProbe(failureOrder);
        failingOwner.UseBehavior(
            new RecordingBehavior<BehaviorOwnerProbe>("created", failureOrder));
        failingOwner.UseBehavior(new FailingCreateBehavior(failureOrder));
        var failureScene = new SceneAggregate("BehaviorCreateFailure");
        CheckThrows<InvalidOperationException>(
            () => failureScene.Add(failingOwner),
            "Behavior creation failure aborts Scene attachment");
        Check(failureScene.FindById(failingOwner.Id) is null &&
              failureOrder.SequenceEqual(new[]
              {
                  "owner.create", "created.create", "failing.create",
                  "created.destroy", "owner.destroy"
              }),
            "Creation failure unwinds initialized Behavior state and removes the owner");

        var lifetimeOwner = new CountingOwner();
        var lifetime = lifetimeOwner.UseBehavior(new LifetimeBehavior(0.05d));
        var lifetimeScene = new SceneAggregate("LifetimeBehavior");
        lifetimeScene.Add(lifetimeOwner);
        lifetimeScene.PerformStep(0.02d);
        Check(lifetimeScene.FindById(lifetimeOwner.Id) is not null &&
              lifetime.RemainingSeconds > 0d,
            "LifetimeBehavior remains active before its owner-time duration");
        lifetimeScene.PerformStep(0.04d);
        Check(lifetimeScene.FindById(lifetimeOwner.Id) is null && lifetime.IsCompleted,
            "LifetimeBehavior requests owner destruction at the safe Step boundary");

        var pauseOwner = new CountingOwner();
        var pauseBehavior = pauseOwner.UseBehavior(new CountingBehavior<CountingOwner>());
        var unscaledOwner = new CountingOwner { TimeMode = InstanceTimeMode.Unscaled };
        var unscaledBehavior = unscaledOwner.UseBehavior(new CountingBehavior<CountingOwner>());
        var pausedScene = new SceneAggregate("PausedBehaviors");
        pausedScene.Add(pauseOwner);
        pausedScene.Add(unscaledOwner);
        var pauseKey = new GameplayPauseKey("behavior-test");
        pausedScene.Time.Pause(pauseKey);
        pausedScene.PerformStep(0.1d);
        Check(pauseBehavior.StepCount == 0 && unscaledBehavior.StepCount == 1,
            "Behavior scheduling inherits Gameplay and Unscaled owner time domains");
        pausedScene.Time.Resume(pauseKey);
        pauseOwner.SetActive(false, _ => { });
        pausedScene.PerformStep(0.1d);
        Check(pauseBehavior.StepCount == 0 && unscaledBehavior.StepCount == 2,
            "Inactive owners also suppress their Behavior lifecycle");

        CheckThrows<ArgumentOutOfRangeException>(
            () => new LifetimeBehavior(double.NaN),
            "LifetimeBehavior rejects non-finite durations");

        var allocationOwner = new CountingOwner();
        allocationOwner.UseBehavior(new CountingBehavior<CountingOwner>());
        allocationOwner.UseBehavior(new CountingBehavior<CountingOwner>());
        var allocationScene = new SceneAggregate("BehaviorAllocation");
        allocationScene.Add(allocationOwner);
        for (int i = 0; i < 64; i++) allocationScene.PerformStep(1d / 60d);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++) allocationScene.PerformStep(1d / 60d);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Behavior lifecycle dispatch remains allocation-free after warmup ({allocated:N0} B)");
    }

    private static void TestSceneCatalogAndPrefabs()
    {
        Console.WriteLine("3. Declarative Scene catalog and Prefabs");
        SceneRef main = new("Main");
        SceneRef gameOver = new("GameOver");
        SceneRef<ResultsSceneArgs> results = new("Results");
        ResultsSceneArgs configuredResults = default;
        var probePrefab = new PrefabRef<HostingPrefabProbe>("hosting.probe");
        var plan = GameApplication.Create()
            .UseDefault2DRenderer()
            .ConfigureInstances(instances => instances.Register(
                probePrefab,
                spawn => new HostingPrefabProbe(spawn.Position)))
            .AddScene(main, _ => { })
            .AddScene(gameOver, _ => { })
            .AddScene(results, (_, args) => configuredResults = args)
            .StartScene(gameOver)
            .BuildPlan();

        HostingPrefabProbe created = plan.Instances.Create(
            probePrefab,
            new PrefabSpawnContext(new Vector2D(4, 5)));
        Check(plan.InitialScene == gameOver && plan.Scenes.Count == 3 &&
              created.Position == new Vector2D(4, 5),
            "Builder freezes a typed Prefab catalog and selects a registered initial Scene");
        CheckThrows<ArgumentException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .AddScene(main, _ => { })
                .AddScene(main, _ => { }),
            "Duplicate Scene names fail during registration");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .AddScene(main, _ => { })
                .StartScene(new SceneRef("Missing"))
                .BuildPlan(),
            "An unregistered initial Scene fails before window creation");

        var navigator = new SceneNavigator(plan.Scenes, main);
        navigator.SwitchTo(gameOver);
        navigator.SwitchTo(gameOver);
        Check(navigator.IsSwitchPending && navigator.TryTakePending(out SceneRef pending) &&
              pending == gameOver,
            "Repeated same-target requests are idempotent and remain frame-boundary pending");
        navigator.Commit(gameOver);
        navigator.SwitchTo(gameOver);
        Check(!navigator.IsSwitchPending && navigator.Current == gameOver,
            "Requesting the current Scene is a no-op after commit");
        CheckThrows<KeyNotFoundException>(
            () => navigator.SwitchTo(new SceneRef("Missing")),
            "Unknown runtime Scene requests fail immediately");

        var typedNavigator = new SceneNavigator(plan.Scenes, main);
        var resultsArgs = new ResultsSceneArgs(42, 12.5d);
        typedNavigator.SwitchTo(results, resultsArgs);
        typedNavigator.SwitchTo(results, resultsArgs);
        Check(typedNavigator.TryTakePending(out ISceneActivation typedActivation) &&
              typedActivation.Scene == results.Untyped &&
              typedActivation.ArgumentsType == typeof(ResultsSceneArgs),
            "Typed Scene requests retain their argument type at the safe boundary");
        plan.Scenes[results.Name].Configure(null!, typedActivation);
        Check(configuredResults == resultsArgs,
            "Typed Scene configuration receives the copied argument snapshot");

        typedNavigator.SwitchTo(results, resultsArgs);
        CheckThrows<InvalidOperationException>(
            () => typedNavigator.SwitchTo(results, resultsArgs with { Score = 99 }),
            "Same-frame requests with different typed arguments are rejected");
        _ = typedNavigator.TryTakePending(out ISceneActivation _);
        CheckThrows<InvalidOperationException>(
            () => typedNavigator.SwitchTo(results.Untyped),
            "An untyped reference cannot activate a typed Scene definition");

        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .AddScene(results, (_, _) => { })
                .BuildPlan(),
            "A typed first Scene requires matching initial arguments");

        ResultsSceneArgs initialArgs = new(7, 2d);
        ResultsSceneArgs configuredInitial = default;
        var typedInitialPlan = GameApplication.Create()
            .UseDefault2DRenderer()
            .AddScene(results, (_, args) => configuredInitial = args)
            .StartScene(results, initialArgs)
            .BuildPlan();
        typedInitialPlan.ConfigureScene(null!);
        Check(typedInitialPlan.InitialScene == results.Untyped && configuredInitial == initialArgs,
            "StartScene carries typed arguments into the initial Scene configuration");
    }

    private static void TestResourceOwnership()
    {
        Console.WriteLine("3. Reverse-order resource ownership");
        var order = new List<string>();
        var stack = new OwnedResourceStack();
        stack.Add(new Probe("shader", order));
        stack.Add(new Probe("target", order));
        stack.Add(new Probe("builder", order));
        stack.Dispose();
        stack.Dispose();
        Check(order.SequenceEqual(new[] { "builder", "target", "shader" }),
            "Resources dispose once in reverse creation order");
        CheckThrows<ObjectDisposedException>(
            () => stack.Add(new Probe("late", order)),
            "Disposed ownership scope rejects late resources");

        order.Clear();
        var failing = new OwnedResourceStack();
        failing.Add(new Probe("first", order));
        failing.Add(new Probe("throws", order, fail: true));
        failing.Add(new Probe("last", order));
        CheckThrows<AggregateException>(failing.Dispose,
            "Disposal reports owned resource failures");
        Check(order.SequenceEqual(new[] { "last", "throws", "first" }),
            "A disposal failure does not skip remaining resources");
    }

    private static void TestDefaultPresentationControllers()
    {
        Console.WriteLine("4. Default renderer domain lifecycle");
        var hdrPlan = new Default2DRendererPlan(
            null,
            null,
            true,
            ToneMappingSettings.Default,
            BloomSettings.Default,
            true,
            true);
        var events = new List<IDomainEvent>();
        var hdr = new DefaultWorldEffectsController(
            events.Add,
            RenderViewRef.Main,
            RenderSurfaceKey.SceneColor,
            hdrPlan.MainEffects);
        hdr.OnCreate();
        Check(events.OfType<RenderEffectRequestedEvent>().Select(value => value.Descriptor.Key.Kind)
                .SequenceEqual(new[]
                {
                    BloomEffectDescriptor.EffectKind,
                    ToneMappingEffectDescriptor.EffectKind
                }),
            "HDR preset declares Bloom then Tone Mapping once for all Viewports");
        events.Clear();
        hdr.OnDestroy();
        Check(events.OfType<RenderEffectReleasedEvent>().Select(value => value.EffectKey.Kind)
                .SequenceEqual(new[]
                {
                    ToneMappingEffectDescriptor.EffectKind,
                    BloomEffectDescriptor.EffectKind
                }),
            "HDR preset releases consumers before producers");

        events.Clear();
        RenderViewRef observer = new("observer");
        RenderSurfaceKey observerScene = new("scene-view", observer.Name, "color");
        var observerHdr = new DefaultWorldEffectsController(
            events.Add,
            observer,
            observerScene,
            RenderViewEffects.Hdr(ToneMappingSettings.Default));
        observerHdr.OnCreate();
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: ToneMappingEffectDescriptor
                {
                    Key.Slot: "observer",
                    Source: var observerSource
                }
            } && observerSource == observerScene,
            "A secondary HDR View owns a distinct effect key and consumes its own Scene Surface");

        events.Clear();
        var viewport = SingleCameraViewportLayoutBuilder.Default.Single();
        var ldr = new DefaultWorldPresentationController(
            events.Add,
            RenderSurfaceKey.SceneColor,
            viewport,
            layer: 0,
            PresentationBlendMode.Opaque);
        ldr.OnCreate();
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: PresentSurfaceDescriptor { Source: var source }
            } && source == RenderSurfaceKey.SceneColor,
            "World Viewport presents its selected source without owning post-process resources");

        events.Clear();
        var gui = new DefaultGuiPresentationController(events.Add);
        gui.OnCreate();
        Check(events.Single() is RenderEffectRequestedEvent
            {
                Descriptor: PresentSurfaceDescriptor
                {
                    Source: var guiSource,
                    Layer: 1000
                }
            } && guiSource == RenderSurfaceKey.SceneGui,
            "SceneGui is declared as an exposure-independent top layer");
    }

    private static void TestPerformanceTelemetry()
    {
        Console.WriteLine("5. Performance budgets and low-frequency telemetry");
        var sink = new RecordingTelemetrySink();
        var telemetry = new PerformanceTelemetryOptions(
            sink,
            TimeSpan.FromSeconds(1),
            new PerformanceBudget(maxDrawCalls: 10));
        var plan = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.EnablePerformanceTelemetry(telemetry))
            .ConfigureScene("Telemetry", _ => { })
            .BuildPlan();
        Check(plan.Renderer.PerformanceTelemetry == telemetry &&
              plan.WindowOptions.FrameStatistics is not null,
            "Enabling telemetry freezes its plan and automatically enables frame statistics");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .EnablePerformanceTelemetry(telemetry)
                .EnablePerformanceTelemetry(telemetry),
            "Telemetry cannot be configured twice");
        CheckThrows<ArgumentOutOfRangeException>(
            () => _ = new PerformanceBudget(maxDrawCalls: -1),
            "Negative performance limits are rejected");

        var frame = new FrameStatisticsSnapshot(1, 60, 60, 12, 6, 3, 7);
        var memory = new GpuMemoryEstimate(
            2, 100,
            1, 200,
            3, 300,
            1, 50,
            1, 25);
        var budget = new PerformanceBudget(
            maxDrawCalls: 11,
            maxBatchFlushes: 6,
            maxTextureSwitches: 2,
            maxActivePasses: 8,
            maxEstimatedGpuMemoryBytes: 674);
        var violations = budget.Evaluate(frame, memory);
        Check(violations.Select(item => item.Metric).SequenceEqual(new[]
              {
                  PerformanceMetric.DrawCalls,
                  PerformanceMetric.TextureSwitches,
                  PerformanceMetric.EstimatedGpuMemoryBytes
              }),
            "Budgets report only strictly exceeded frame and memory limits");

        Check(RenderTargetMemoryEstimator.EstimateBytes(
                  new RenderTargetDescriptor(10, 20)) == 800 &&
              RenderTargetMemoryEstimator.EstimateBytes(
                  new RenderTargetDescriptor(
                      10, 20,
                      RenderTargetColorFormat.Rgba16Float,
                      RenderTargetDepthStencilFormat.Depth24Stencil8)) == 2400,
            "RenderTarget estimates include declared color and depth/stencil formats");

        long timestamp = 0;
        int captures = 0;
        var sampler = new PerformanceTelemetrySampler(
            telemetry,
            () =>
            {
                captures++;
                return new RuntimePerformanceSnapshot(
                    DateTimeOffset.UnixEpoch,
                    frame,
                    default,
                    null!,
                    memory,
                    Array.Empty<CustomGpuMemoryDiagnostics>(),
                    violations);
            },
            () => timestamp,
            timestampFrequency: 1000);
        Check(sampler.Tick() && captures == 1 && sink.Snapshots.Count == 1,
            "First completed frame publishes immediately");
        timestamp = 999;
        Check(!sampler.Tick() && captures == 1,
            "Frames inside the interval do not capture or publish");
        timestamp = 1000;
        Check(sampler.Tick() && captures == 2 && sink.Snapshots.Count == 2,
            "The next interval publishes one fresh snapshot");
    }

    private static void TestContentHotReloadOptions()
    {
        Console.WriteLine("6. Content hot reload configuration boundary");
        var sink = new RecordingHotReloadSink();
        var options = new ContentHotReloadOptions(
            sink,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200));
        var package = new ContentPackageRef("game.assets", "game/assets.json");
        var plan = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer
                .UseContent(package)
                .EnableContentHotReload(options))
            .ConfigureScene("HotReload", _ => { })
            .BuildPlan();
        Check(plan.Renderer.ContentHotReload == options,
            "Hot reload options are frozen into the renderer plan");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .EnableContentHotReload(options)
                .ToPlan()
                .Validate(),
            "Hot reload requires an explicitly configured content package");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .UseContent(package)
                .EnableContentHotReload(options)
                .EnableContentHotReload(options),
            "Hot reload cannot be configured twice");
        CheckThrows<ArgumentOutOfRangeException>(
            () => _ = new ContentHotReloadOptions(sink, TimeSpan.Zero),
            "Hot reload polling interval must be positive");
    }

    private static void TestContentHotReloadCoordinator()
    {
        Console.WriteLine("7. Content revision debounce, apply, and failure fallback");
        string root = Directory.CreateTempSubdirectory("mygame-hosting-reload-").FullName;
        try
        {
            string imagePath = Path.Combine(root, "live.webp");
            WriteWebp(imagePath, 2, SKColors.Red);
            WriteContentManifest(root);
            WriteContentRevision(root, "revision-1");

            var backend = new HotReloadTextureBackend();
            using var textures = new TextureLibrary(backend);
            var sprites = new SpriteLibrary(textures);
            using var manager = new ContentPackageManager(textures, sprites, root);
            var packageRef = new ContentPackageRef("hosting.reload", "assets.json");
            using var package = manager.Load(packageRef);
            var sink = new RecordingHotReloadSink();
            var time = new ManualTimeProvider();
            using var coordinator = new ContentHotReloadCoordinator(
                manager,
                packageRef,
                new ContentHotReloadOptions(
                    sink,
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(20)),
                time);

            WriteWebp(imagePath, 4, SKColors.Blue);
            WriteContentRevision(root, "revision-2");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            Check(sink.Diagnostics.Select(item => item.Status)
                    .SequenceEqual(new[] { ContentHotReloadStatus.Detected }),
                "A changed stable fingerprint is detected before preparation");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            Check(sink.Diagnostics.Count == 1,
                "Debounce prevents an early revision preparation");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            SpinUntilTerminal(coordinator, sink, ContentHotReloadStatus.Applied);
            textures.TryGetMetadata(package.GetTexture("hosting.texture"), out var applied);
            Check(applied.Width == 4 && sink.Diagnostics[^1].Status == ContentHotReloadStatus.Applied,
                "A prepared revision commits at a later frame boundary");

            File.WriteAllBytes(imagePath, [1, 2, 3]);
            WriteContentRevision(root, "revision-bad");
            time.Advance(TimeSpan.FromMilliseconds(10));
            coordinator.Tick();
            time.Advance(TimeSpan.FromMilliseconds(20));
            coordinator.Tick();
            SpinUntilTerminal(coordinator, sink, ContentHotReloadStatus.Failed);
            int failures = sink.Diagnostics.Count(item => item.Status == ContentHotReloadStatus.Failed);
            textures.TryGetMetadata(package.GetTexture("hosting.texture"), out var afterFailure);
            time.Advance(TimeSpan.FromSeconds(1));
            coordinator.Tick();
            Check(afterFailure.Width == 4 &&
                  sink.Diagnostics.Count(item => item.Status == ContentHotReloadStatus.Failed) == failures,
                "A failed fingerprint keeps the old resource and is not retried every poll");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestShaderHotReloadConfiguration()
    {
        Console.WriteLine("8. Shader file snapshots and hot reload configuration");
        string root = Directory.CreateTempSubdirectory("mygame-shader-reload-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "sprite.vert"), "vertex-v1");
            File.WriteAllText(Path.Combine(root, "sprite.frag"), "fragment-v1");
            string assetManifest = Path.Combine(root, "shaders.json");
            File.WriteAllText(assetManifest,
                """
                {
                  "schemaVersion":1,
                  "shaders":[
                    {"name":"game.sprite","vertex":"sprite.vert","fragment":"sprite.frag"}
                  ],
                  "materials":[
                    {
                      "name":"game.sprite.material",
                      "shader":"game.sprite",
                      "uniforms":[
                        {"name":"uGain","type":"float","default":1.5}
                      ]
                    }
                  ]
                }
                """);
            var definition = new ShaderFileDefinition(
                "game.sprite",
                "sprite.vert",
                "sprite.frag");
            ShaderFileSetSnapshot first = ShaderFileSetReader.Read(root, new[] { definition });
            File.WriteAllText(Path.Combine(root, "sprite.frag"), "fragment-v2");
            ShaderFileSetSnapshot second = ShaderFileSetReader.Read(root, new[] { definition });
            Check(first.Fingerprint != second.Fingerprint &&
                  second.ChangedNamesFrom(first).SequenceEqual(new[] { "game.sprite" }),
                "Source content hashes identify the exact changed Shader program");
            Check(second.Sources.Single().VertexPath == Path.Combine(root, "sprite.vert") &&
                  second.Sources.Single().FragmentPath == Path.Combine(root, "sprite.frag"),
                "Stable snapshots retain exact source paths for driver diagnostics");

            var buildError = new ShaderBuildException(
                "game.sprite",
                "FragmentShader",
                "ERROR: 0:17: unexpected token",
                Path.Combine(root, "sprite.frag"));
            Check(buildError.SourceLine == 17 &&
                  buildError.Message.Contains("sprite.frag':17", StringComparison.Ordinal),
                "Driver logs are enriched with the source path and parsed line number");

            var sink = new RecordingShaderHotReloadSink();
            var options = new ShaderHotReloadOptions(
                sink,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200));
            var plan = GameApplication.Create()
                .UseDefault2DRenderer(renderer => renderer
                    .UseShaders(root, definition)
                    .EnableShaderHotReload(options))
                .ConfigureScene("Shaders", _ => { })
                .BuildPlan();
            Check(plan.Renderer.ShaderRoot == root &&
                  plan.Renderer.ShaderFiles?.Single() == definition &&
                  plan.Renderer.ShaderHotReload == options,
                "Shader files and hot reload policy are frozen into the renderer plan");

            var assetPlan = GameApplication.Create()
                .UseDefault2DRenderer(renderer => renderer.UseShaderAssets(assetManifest))
                .ConfigureScene("ShaderAssets", _ => { })
                .BuildPlan();
            var declaredMaterial = assetPlan.Renderer.ShaderMaterials!.Single();
            Check(assetPlan.Renderer.ShaderAssetManifestPath == assetManifest &&
                  assetPlan.Renderer.ShaderRoot == root &&
                  assetPlan.Renderer.ShaderFiles?.Single().Name == "game.sprite" &&
                  declaredMaterial.Name == "game.sprite.material" &&
                  declaredMaterial.Uniforms.Single().DefaultValue.FloatValue == 1.5f,
                "Declarative Shader assets freeze programs, Material schema, and defaults");

            CheckThrows<InvalidOperationException>(
                () => new Default2DRendererOptions()
                    .EnableShaderHotReload(options)
                    .ToPlan()
                    .Validate(),
                "Shader hot reload requires registered file-backed Shaders");
            CheckThrows<ArgumentException>(
                () => new Default2DRendererOptions().UseShaders(
                    root,
                    definition,
                    definition),
                "Duplicate logical Shader names are rejected before GL initialization");
            CheckThrows<InvalidOperationException>(
                () => new Default2DRendererOptions()
                    .UseShaders(root, definition)
                    .UseShaderAssets(assetManifest),
                "Imperative and declarative Shader registration cannot overlap");
            CheckThrows<InvalidDataException>(
                () => ShaderFileSetReader.Read(root, new[]
                {
                    new ShaderFileDefinition("escape", "../outside.vert", "sprite.frag")
                }),
                "Shader source paths cannot escape their configured root");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void SpinUntilTerminal(
        ContentHotReloadCoordinator coordinator,
        RecordingHotReloadSink sink,
        ContentHotReloadStatus terminal)
    {
        for (int i = 0; i < 200; i++)
        {
            coordinator.Tick();
            if (sink.Diagnostics.Any(item => item.Status == terminal)) return;
            Thread.Sleep(2);
        }
        throw new TimeoutException($"Content hot reload did not report {terminal}.");
    }

    private static void WriteContentManifest(string root) => File.WriteAllText(
        Path.Combine(root, "assets.json"),
        """
        { "schemaVersion":1, "id":"hosting.reload", "dependencies":[],
          "textures":[{"name":"hosting.texture","path":"live.webp"}],
          "sprites":[{"name":"hosting.sprite","layout":"single","texture":"hosting.texture",
            "origin":{"x":0,"y":0}}] }
        """);

    private static void WriteContentRevision(string root, string fingerprint) => File.WriteAllText(
        Path.Combine(root, CompiledContentRevisionReader.MetadataFileName),
        $$"""
        { "schemaVersion":1, "owner":"MyGameEngine.AssetCompiler", "compilerVersion":"2",
          "rootPackageId":"hosting.reload", "rootManifest":"assets.json",
          "inputFingerprint":"{{fingerprint}}" }
        """);

    private static void WriteWebp(string path, int size, SKColor color)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, 100)
            ?? throw new InvalidOperationException("Could not encode WebP fixture.");
        File.WriteAllBytes(path, data.ToArray());
    }

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
            return;
        }
        _failures++;
        Console.WriteLine($"  [FAIL] {name}");
    }

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

    private sealed class Probe(
        string name,
        List<string> order,
        bool fail = false) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            order.Add(name);
            if (fail) throw new InvalidOperationException(name);
        }
    }

    private sealed class HostingPrefabProbe : GameInstance
    {
        public HostingPrefabProbe(Vector2D position) => Position = position;
    }

    private sealed class TaggedProbe : GameInstance
    {
        public TaggedProbe(Vector2D position, params GameplayTag[] tags)
        {
            Position = position;
            Collider = CollisionShape2D.Circle(2f);
            for (int i = 0; i < tags.Length; i++)
                AddTag(tags[i]);
        }
    }

    private sealed class BehaviorOwnerProbe(List<string> order) : GameInstance
    {
        public override void OnCreate() => order.Add("owner.create");
        public override void OnBeginStep(double deltaTime) => order.Add("owner.begin");
        public override void OnStep(double deltaTime) => order.Add("owner.step");
        public override void OnEndStep(double deltaTime) => order.Add("owner.end");
        public override void OnDestroy() => order.Add("owner.destroy");
    }

    private sealed class RecordingBehavior<TOwner> : GameplayBehavior<TOwner>
        where TOwner : GameInstance
    {
        private readonly List<string> _order;
        private readonly string _create;
        private readonly string _begin;
        private readonly string _step;
        private readonly string _end;
        private readonly string _destroy;

        public RecordingBehavior(string name, List<string> order)
        {
            _order = order;
            _create = $"{name}.create";
            _begin = $"{name}.begin";
            _step = $"{name}.step";
            _end = $"{name}.end";
            _destroy = $"{name}.destroy";
        }

        public override void OnCreate() => _order.Add(_create);
        public override void OnBeginStep(double deltaTime) => _order.Add(_begin);
        public override void OnStep(double deltaTime) => _order.Add(_step);
        public override void OnEndStep(double deltaTime) => _order.Add(_end);
        public override void OnDestroy() => _order.Add(_destroy);
    }

    private sealed class FailingCreateBehavior(List<string> order)
        : GameplayBehavior<BehaviorOwnerProbe>
    {
        public override void OnCreate()
        {
            order.Add("failing.create");
            throw new InvalidOperationException("Expected Behavior creation failure.");
        }
    }

    private sealed class CountingOwner : GameInstance { }

    private sealed class CooldownProbe : GameInstance
    {
        public GameplayCooldown Cooldown { get; }

        public CooldownProbe(double durationSeconds) =>
            Cooldown = new GameplayCooldown(durationSeconds);

        public override void OnStep(double deltaTime) => Cooldown.Update(deltaTime);
    }

    private sealed class CountingBehavior<TOwner> : GameplayBehavior<TOwner>
        where TOwner : GameInstance
    {
        public int StepCount { get; private set; }
        public override void OnStep(double deltaTime) => StepCount++;
    }

    private sealed class LogicalInputProbe(
        InputActionRef fire,
        InputAxis2DRef move) : GameInstance
    {
        public bool FireDown => ActionDown(fire);
        public Vector2D Move => InputAxis2D(move);
    }

    private sealed class BufferedInputProbe : GameInstance
    {
        public InputActionBuffer Buffer { get; }

        public BufferedInputProbe(InputActionRef action, double windowSeconds) =>
            Buffer = new InputActionBuffer(action, windowSeconds);

        public override void OnStep(double deltaTime) =>
            UpdateActionBuffer(Buffer, deltaTime);
    }

    private sealed class MappedInputProbe : IInputProvider
    {
        public HashSet<InputKey> Down { get; } = [];
        public HashSet<InputKey> Pressed { get; } = [];
        public HashSet<InputKey> Released { get; } = [];
        public Vector2D MousePosition => Vector2D.Zero;
        public float MouseScrollDelta => 0f;

        public bool IsKeyDown(InputKey key) => Down.Contains(key);
        public bool WasKeyPressed(InputKey key) => Pressed.Contains(key);
        public bool WasKeyReleased(InputKey key) => Released.Contains(key);
        public bool IsMouseButtonDown(MouseButton button) => false;
    }

    private readonly record struct ResultsSceneArgs(int Score, double ElapsedSeconds);

    private sealed class RecordingTelemetrySink : IPerformanceTelemetrySink
    {
        public List<RuntimePerformanceSnapshot> Snapshots { get; } = new();

        public void Publish(RuntimePerformanceSnapshot snapshot) => Snapshots.Add(snapshot);
    }

    private sealed class RecordingHotReloadSink : IContentHotReloadSink
    {
        public List<ContentHotReloadDiagnostic> Diagnostics { get; } = [];

        public void Publish(ContentHotReloadDiagnostic diagnostic) => Diagnostics.Add(diagnostic);
    }

    private sealed class RecordingShaderHotReloadSink : IShaderHotReloadSink
    {
        public List<ShaderHotReloadDiagnostic> Diagnostics { get; } = [];

        public void Publish(ShaderHotReloadDiagnostic diagnostic) => Diagnostics.Add(diagnostic);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class HotReloadTextureBackend : ITextureBackend
    {
        private uint _next = 1;

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler) => _next++;

        public void DeleteTexture(uint handle)
        {
        }
    }
}
