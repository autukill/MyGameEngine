namespace GameEngine.Hosting.Tests;

using System.Numerics;
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
using GameEngine.Features.Audio;
using GameEngine.Features.Presentation.Domain;
using GameEngine.Features.RenderPipeline.Domain;
using GameEngine.Features.Replay.Application;
using GameEngine.Features.Replay.Domain;
using GameEngine.Features.ToneMapping.Domain;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using GameEngine.Features.ViewportNavigation;
using SkiaSharp;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        Console.WriteLine("=== Engine Hosting Smoke Test ===\n");
        TestBuilderPlans();
        TestSceneCameraViewportPolicy();
        TestBuilderValidation();
        TestSceneAudioScope();
        TestLogicalInputMap();
        TestLogicalInputRecordingAndPlayback();
        TestGameplayCooldown();
        TestGameplayHealth();
        TestInstanceReferences();
        TestDeterministicSimulationPrimitives();
        TestGameplayStateHashing();
        TestGameplaySignals();
        TestSpawnSequences();
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
            .UseAudio(new AudioHostingOptions(MaxVoices: 48, ForceSilentBackend: true))
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
              plan.Audio is { MaxVoices: 48, ForceSilentBackend: true } &&
              plan.Renderer.ResolvedViewports.Single().Slot == ViewportSlotRef.Main,
            "Builder freezes window, audio, content, HDR, Bloom, Stencil, and Scene configuration");

        var ldr = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.DisableSceneGui())
            .ConfigureScene("Ldr", _ => { })
            .BuildPlan();
        Check(!ldr.Renderer.HdrEnabled &&
              ldr.Renderer.Bloom is null &&
              !ldr.Renderer.SceneGuiEnabled,
            "Default renderer remains LDR and optional features are lazy");

        var homePackage = new ContentPackageRef("game.home", "Home/assets.json");
        SceneRef homeScene = new("Home");
        var sceneContent = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseContentCatalog())
            .AddScene(homeScene, homePackage, _ => { })
            .BuildPlan();
        Check(sceneContent.Renderer.ContentCatalogOnly &&
              sceneContent.Renderer.ContentPackagesRoot == "AssetsCompiled" &&
              sceneContent.Renderer.ContentManifest is null &&
              sceneContent.Scenes[homeScene.Name].ContentPackage == homePackage,
            "Scene content catalogs retain package declarations without eager root loading");

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

        var interactiveViewport = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseRenderViews(views => views
                .ConfigureMain(
                    ViewportRect.LeftHalf,
                    navigation: viewport => viewport
                        .Drag()
                        .Pinch()
                        .Wheel(new ViewportWheelOptions(smoothFrames: 4))
                        .MouseEdges()
                        .Decelerate()
                        .SnapZoom(new ViewportSnapZoomOptions(visibleWidth: 1_200f))
                        .ClampZoom(new ViewportClampZoomOptions(
                            maxWidth: 12_000f,
                            maxHeight: 12_000f))
                        .Clamp(new ViewportClampOptions(
                            new Bounds2D(0f, 0f, 12_000f, 12_000f))))
                .Add("observer", ViewportRect.RightHalf)))
            .ConfigureScene("InteractiveMap", _ => { })
            .BuildPlan();
        ViewportNavigationConfiguration? navigation =
            interactiveViewport.Renderer.RenderViews![0].Navigation;
        Check(navigation is not null &&
              navigation.Drag == ViewportDragOptions.Default &&
              navigation.Pinch == ViewportPinchOptions.Default &&
              navigation.Wheel?.SmoothFrames == 4 &&
              navigation.MouseEdges == ViewportMouseEdgesOptions.Default &&
              navigation.Decelerate == ViewportDecelerateOptions.Default &&
              navigation.SnapZoom?.VisibleWidth == 1_200f &&
              navigation.ClampZoom?.MaxWidth == 12_000f &&
              navigation.Clamp?.Underflow == ViewportUnderflow.Center &&
              interactiveViewport.Renderer.RenderViews[1].Navigation is null,
            "Render View plans freeze an independent interactive Viewport plugin chain");

        var singleInteractiveViewport = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseInteractiveViewport(viewport => viewport
                .Drag()
                .Pinch()
                .Wheel()
                .Decelerate()
                .Clamp(new ViewportClampOptions(
                    new Bounds2D(0f, 0f, 12_000f, 12_000f)))))
            .ConfigureScene("SingleInteractiveMap", _ => { })
            .BuildPlan();
        Check(singleInteractiveViewport.Renderer.RenderViews is null &&
              singleInteractiveViewport.Renderer.MainNavigation?.Drag is not null &&
              singleInteractiveViewport.Renderer.MainNavigation?.Pinch is not null &&
              singleInteractiveViewport.Renderer.MainNavigation?.Clamp is not null,
            "One main Camera enables interactive Viewport navigation without fake multi-View setup");

        SceneRef fixedScene = new("FixedScene");
        SceneRef mapScene = new("MapScene");
        var sceneViews = GameApplication.Create()
            .UseDefault2DRenderer()
            .AddScene(
                fixedScene,
                views => views.ConfigureMain(
                    new SceneCameraState(new Vector2(120f, 0f)),
                    viewportPolicy: SceneCameraViewportPolicy.FixedVisibleHeight(720f, 1_280f)),
                _ => { })
            .AddScene(
                mapScene,
                views => views.ConfigureMain(
                    new SceneCameraState(new Vector2(164f, 14_820f)),
                    navigation: navigation => navigation
                        .Drag(new ViewportDragOptions(ViewportAxis.Vertical, 8f))
                        .Decelerate()
                        .Bounce(new ViewportBounceOptions(
                            new Bounds2D(0f, 0f, 1_048f, 16_100f),
                            ViewportAxis.Vertical))),
                _ => { })
            .StartScene(fixedScene)
            .BuildPlan();
        SceneRenderViewDefinition fixedMain =
            sceneViews.Scenes[fixedScene.Name].Views![RenderViewRef.Main.Name];
        SceneRenderViewDefinition mapMain =
            sceneViews.Scenes[mapScene.Name].Views![RenderViewRef.Main.Name];
        Check(fixedMain.Camera.Position == new Vector2(120f, 0f) &&
              fixedMain.ViewportPolicy.Mode == SceneCameraViewportMode.FixedVisibleHeight &&
              fixedMain.ViewportPolicy.ReferenceViewportSize == new Vector2(720f, 1_280f) &&
              fixedMain.Navigation is null &&
              mapMain.Camera.Position == new Vector2(164f, 14_820f) &&
              mapMain.Navigation?.Drag?.Axis == ViewportAxis.Vertical &&
              mapMain.Navigation?.Bounce?.WorldBounds.Bottom == 16_100f,
            "Each Scene freezes independent Camera and Viewport navigation ownership");
    }

    private static void TestSceneCameraViewportPolicy()
    {
        Console.WriteLine("1b. Scene Camera Viewport resize policy");
        SceneCameraViewportPolicy policy =
            SceneCameraViewportPolicy.FixedVisibleHeight(720f, 1_280f);
        var camera = new Camera2D(new Vector2(720f, 1_280f));
        var state = new SceneCameraState(new Vector2(120f, 0f));
        policy.Activate(camera, state);
        Check(camera.Position == state.Position && camera.Zoom == 1f,
            "Reference-size activation preserves authored Camera coordinates");

        policy.Resize(camera, 360f, 640f);
        Check(camera.Position == state.Position && MathF.Abs(camera.Zoom - .5f) < .000001f &&
              camera.TryGetStableVisibleWorldBounds(out Bounds2D halfBounds) &&
              MathF.Abs(halfBounds.Width - 720f) < .001f &&
              MathF.Abs(halfBounds.Height - 1_280f) < .001f,
            "Proportional window shrink scales pixels without shrinking the visible world");

        policy.Resize(camera, 480f, 640f);
        Check(camera.TryGetStableVisibleWorldBounds(out Bounds2D wideBounds) &&
              MathF.Abs(wideBounds.Left) < .001f &&
              MathF.Abs(wideBounds.Width - 960f) < .001f &&
              MathF.Abs(wideBounds.Height - 1_280f) < .001f,
            "A wider aspect reveals more centered world space at the fixed visible height");

        var lateCamera = new Camera2D(new Vector2(480f, 640f));
        policy.Activate(lateCamera, state);
        Check(lateCamera.TryGetStableVisibleWorldBounds(out Bounds2D lateBounds) &&
              MathF.Abs(lateBounds.Center.X - wideBounds.Center.X) < .001f &&
              MathF.Abs(lateBounds.Center.Y - wideBounds.Center.Y) < .001f &&
              MathF.Abs(lateBounds.Width - 960f) < .001f,
            "Scene activation after an earlier resize resolves the same centered View");

        SceneCameraViewportPolicy expand =
            SceneCameraViewportPolicy.Expand(720f, 1_280f);
        var narrowExpandCamera = new Camera2D(new Vector2(360f, 800f));
        expand.Activate(narrowExpandCamera, state);
        Check(narrowExpandCamera.TryGetStableVisibleWorldBounds(out Bounds2D expandBounds) &&
              MathF.Abs(expandBounds.Left - 120f) < .001f &&
              expandBounds.Top < 0f &&
              MathF.Abs(expandBounds.Width - 720f) < .001f &&
              MathF.Abs(expandBounds.Height - 1_600f) < .001f,
            "Expand chooses the smaller fit scale and keeps the full reference frame visible");

        SceneCameraViewportPolicy cover =
            SceneCameraViewportPolicy.Cover(720f, 1_280f);
        var narrowCoverCamera = new Camera2D(new Vector2(360f, 800f));
        cover.Activate(narrowCoverCamera, state);
        Check(narrowCoverCamera.TryGetStableVisibleWorldBounds(out Bounds2D coverBounds) &&
              coverBounds.Left > 120f && coverBounds.Right < 840f &&
              MathF.Abs(coverBounds.Width - 576f) < .001f &&
              MathF.Abs(coverBounds.Height - 1_280f) < .001f,
            "Cover chooses the larger fill scale and crops only the surplus reference axis");

        SceneCameraViewportPolicy fixedWidth =
            SceneCameraViewportPolicy.FixedVisibleWidth(720f, 1_280f);
        var fixedWidthCamera = new Camera2D(new Vector2(360f, 800f));
        fixedWidth.Activate(fixedWidthCamera, state);
        Check(fixedWidthCamera.TryGetStableVisibleWorldBounds(out Bounds2D fixedWidthBounds) &&
              MathF.Abs(fixedWidthBounds.Width - 720f) < .001f &&
              MathF.Abs(fixedWidthBounds.Height - 1_600f) < .001f,
            "FixedVisibleWidth preserves width and lets height follow the output aspect");

        narrowExpandCamera.Zoom *= 2f;
        expand.Resize(narrowExpandCamera, 720f, 1_280f);
        Check(MathF.Abs(narrowExpandCamera.Zoom - 2f) < .000001f,
            "Framing resize preserves navigation-authored relative Zoom");

        var rotatedCamera = new Camera2D(new Vector2(720f, 1_280f));
        expand.Activate(rotatedCamera, new SceneCameraState(
            new Vector2(30f, 40f), 1f, .35f));
        bool hadCenter = rotatedCamera.TryViewportToWorld(
            rotatedCamera.ViewportSize * .5f,
            out Vector2 rotatedCenterBefore);
        expand.Resize(rotatedCamera, 480f, 640f);
        bool hasCenter = rotatedCamera.TryViewportToWorld(
            rotatedCamera.ViewportSize * .5f,
            out Vector2 rotatedCenterAfter);
        Check(hadCenter && hasCenter &&
              Vector2.Distance(rotatedCenterBefore, rotatedCenterAfter) < .001f,
            "Framing resize preserves the world center of a rotated Camera");

        var nativeCamera = new Camera2D(new Vector2(720f, 1_280f));
        SceneCameraViewportPolicy.MatchRenderTarget.Activate(nativeCamera, state);
        SceneCameraViewportPolicy.MatchRenderTarget.Resize(nativeCamera, 360f, 640f);
        Check(nativeCamera.Position == state.Position && nativeCamera.Zoom == 1f &&
              nativeCamera.ViewportSize == new Vector2(360f, 640f),
            "Existing MatchRenderTarget behavior remains the default");

        SceneCameraViewportPolicy boundedHeight =
            SceneCameraViewportPolicy.FixedVisibleHeight(720f, 1_280f)
                .WithMaximumVisibleSize(960f, 1_280f);
        SceneCameraFramingResult landscape = boundedHeight.Resolve(1_920, 1_080);
        Check(landscape.OutputWidth == 1_920 && landscape.OutputHeight == 1_080 &&
              landscape.ContentWidth == 810 && landscape.ContentHeight == 1_080 &&
              landscape.HasLetterbox &&
              MathF.Abs(landscape.VisibleWorldSize.X - 960f) < .001f &&
              MathF.Abs(landscape.VisibleWorldSize.Y - 1_280f) < .001f &&
              MathF.Abs(landscape.ContentRect.X - .2890625f) < .000001f &&
              MathF.Abs(landscape.ContentRect.Width - .421875f) < .000001f,
            "Bounded FixedVisibleHeight returns a centered ContentRect instead of exposing world beyond its maximum");
        ViewportPlacement fittedContent = ViewportPlacement.Calculate(
            landscape.ContentWidth,
            landscape.ContentHeight,
            landscape.OutputWidth,
            landscape.OutputHeight,
            ViewportRect.FullScreen,
            ViewportFitMode.Contain);
        Vector2 mappedCenter = fittedContent.ScreenToSource(
            landscape.OutputWidth * .5f,
            landscape.OutputHeight * .5f,
            landscape.ContentWidth,
            landscape.ContentHeight);
        Check(fittedContent.X == 555 && fittedContent.Width == 810 &&
              !fittedContent.Contains(100f, 540f) &&
              Vector2.Distance(mappedCenter, new Vector2(405f, 540f)) < .001f,
            "Bounded content presents with matching letterbox geometry and excludes bars from input mapping");

        SceneCameraFramingResult portrait = boundedHeight.Resolve(400, 1_280);
        Check(!portrait.HasLetterbox && portrait.ContentWidth == 400 &&
              portrait.ContentHeight == 1_280 &&
              MathF.Abs(portrait.VisibleWorldSize.X - 400f) < .001f,
            "A bounded policy remains full-output while the visible world is inside its authored limit");

        SceneCameraViewportPolicy boundedExpand =
            SceneCameraViewportPolicy.Expand(960f, 540f)
                .WithMaximumVisibleSize(1_280f, 720f);
        SceneCameraFramingResult ultraWide = boundedExpand.Resolve(3_440, 900);
        Check(ultraWide.ContentWidth == 2_133 && ultraWide.ContentHeight == 900 &&
              ultraWide.HasLetterbox && ultraWide.VisibleWorldSize.X <= 1_280f + 1f &&
              MathF.Abs(ultraWide.VisibleWorldSize.Y - 540f) < .001f,
            "Bounded Expand caps only the surplus axis at an extreme aspect ratio");

        SceneCameraViewportPolicy topAnchored =
            SceneCameraViewportPolicy.Expand(720f, 1_280f).WithAnchor(.5f, 0f);
        var anchoredCamera = new Camera2D(new Vector2(720f, 1_280f));
        SceneCameraFramingResult anchoredBefore = topAnchored.Activate(
            anchoredCamera,
            new SceneCameraState(new Vector2(120f, 0f)),
            720,
            1_280);
        bool hadAnchor = anchoredCamera.TryViewportToWorld(
            new Vector2(360f, 0f),
            out Vector2 worldAnchorBefore);
        SceneCameraFramingResult anchoredAfter = topAnchored.Resize(
            anchoredCamera,
            anchoredBefore,
            720,
            1_600);
        bool hasAnchor = anchoredCamera.TryViewportToWorld(
            new Vector2(anchoredAfter.ContentWidth * .5f, 0f),
            out Vector2 worldAnchorAfter);
        Check(hadAnchor && hasAnchor &&
              Vector2.Distance(worldAnchorBefore, worldAnchorAfter) < .001f &&
              anchoredCamera.TryGetStableVisibleWorldBounds(out Bounds2D anchoredBounds) &&
              MathF.Abs(anchoredBounds.Top) < .001f,
            "A top-center framing anchor keeps authored top content stable when height expands");
    }

    private static void TestSceneAudioScope()
    {
        Console.WriteLine("1c. Scene-scoped audio ownership");
        var library = new AudioLibrary();
        AudioClipRef hit = library.Register(
            "scene.hit", "memory://scene-hit", new AudioClipMetadata(TimeSpan.FromSeconds(1), 1, 48_000));
        AudioClipRef music = library.Register(
            "scene.music", "memory://scene-music", new AudioClipMetadata(TimeSpan.FromMinutes(2), 2, 48_000));
        using var backend = new SceneAudioTestBackend();
        using var audio = new AudioRuntime(library, backend, maxVoices: 4);
        var scope = new SceneAudioScope(audio);

        AudioPlayOptions sfx = AudioPlayOptions.Sfx;
        AudioVoiceRef hitVoice = scope.Play(hit, in sfx);
        AudioVoiceRef musicVoice = scope.PlayMusic(music);
        Check(scope.Enabled && scope.TrackedVoiceCount == 2 &&
              audio.TryGetSnapshot(musicVoice, out AudioVoiceSnapshot musicSnapshot) &&
              musicSnapshot.Bus == AudioBusRef.Music && musicSnapshot.Loop && musicSnapshot.Priority == 100,
            "SceneAudio tracks SFX and provides a high-priority looping Music convenience path");

        backend.CompleteOldest();
        audio.Update();
        scope.PruneCompleted();
        Check(scope.TrackedVoiceCount == 1 && !scope.IsPlaying(hitVoice),
            "Completed one-shot Voices are pruned without growing the Scene scope forever");

        AudioVoiceRef globalVoice = audio.Play(hit, in sfx);
        scope.StopAll();
        Check(scope.TrackedVoiceCount == 0 && !audio.IsPlaying(musicVoice) && audio.IsPlaying(globalVoice),
            "Ending a Scene stops only scoped Voices and preserves deliberately global playback");
        scope.StopAll();
        Check(audio.IsPlaying(globalVoice), "SceneAudio StopAll is idempotent");

        var disabled = new SceneAudioScope(audio: null);
        Check(!disabled.Enabled, "SceneAudio exposes whether Hosting audio is enabled");
        CheckThrows<InvalidOperationException>(() => disabled.Play(hit, in sfx),
            "SceneAudio playback gives the same explicit UseAudio diagnostic when disabled");
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
            () => new Default2DRendererOptions().UseContentCatalog(" "),
            "Empty content catalog root is rejected");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .AddScene(
                    new SceneRef("Scoped"),
                    new ContentPackageRef("scoped.assets", "Scoped/assets.json"),
                    _ => { })
                .BuildPlan(),
            "Scene package declarations require an explicit content catalog");
        CheckThrows<ArgumentException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer(renderer => renderer.UseContentCatalog())
                .AddScene(new SceneRef("Scoped"), default(ContentPackageRef), _ => { }),
            "Default Scene content package references are rejected during registration");
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
            () => new Default2DRendererOptions().UseRenderViews(views => views
                .ConfigureMain(
                    ViewportRect.LeftHalf,
                    cameraFollow: CameraFollowSettings.Default,
                    navigation: viewport => viewport.Drag())
                .Add("second", ViewportRect.RightHalf)),
            "Camera follow and direct interactive Viewport ownership cannot conflict");
        CheckThrows<InvalidOperationException>(
            () => new Default2DRendererOptions()
                .UseInteractiveViewport(viewport => viewport.Drag())
                .UseInteractiveViewport(viewport => viewport.Wheel()),
            "Main interactive Viewport configuration cannot be registered twice");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .UseDefault2DRenderer()
                .ConfigureScene(
                    "UnknownView",
                    views => views.Configure(
                        new RenderViewRef("missing"),
                        SceneCameraState.Default),
                    _ => { })
                .BuildPlan(),
            "A Scene cannot configure a Render View absent from the renderer layout");
        CheckThrows<ArgumentException>(
            () => new SceneViewLayoutBuilder().ConfigureMain(default),
            "Default-initialized Scene Camera state is rejected explicitly");
        CheckThrows<ArgumentOutOfRangeException>(
            () => SceneCameraViewportPolicy.FixedVisibleHeight(0f, 1_280f),
            "Fixed-height Scene Camera policies reject an invalid reference width");
        CheckThrows<ArgumentOutOfRangeException>(
            () => SceneCameraViewportPolicy.FixedVisibleHeight(720f, float.NaN),
            "Fixed-height Scene Camera policies reject an invalid visible height");
        CheckThrows<ArgumentOutOfRangeException>(
            () => SceneCameraViewportPolicy.Expand(720f, 1_280f).WithAnchor(1.1f, .5f),
            "Scene Camera framing anchors remain normalized");
        CheckThrows<ArgumentOutOfRangeException>(
            () => SceneCameraViewportPolicy.FixedVisibleHeight(720f, 1_280f)
                .WithMaximumVisibleSize(700f, 1_280f),
            "Scene Camera visible limits cannot be smaller than the authored reference View");
        CheckThrows<InvalidOperationException>(
            () => SceneCameraViewportPolicy.Cover(720f, 1_280f)
                .WithMaximumVisibleSize(960f, 1_280f),
            "Cropping Camera policies reject the non-cropping letterbox limit API");
        CheckThrows<ArgumentException>(
            () => new SceneViewLayoutBuilder().ConfigureMain(
                SceneCameraState.Default,
                CameraFollowSettings.Default,
                navigation => navigation.Drag()),
            "A Scene View cannot combine Camera follow and interactive navigation");
        CheckThrows<InvalidOperationException>(
            () => new SceneViewLayoutBuilder()
                .ConfigureMain(SceneCameraState.Default)
                .ConfigureMain(SceneCameraState.Default),
            "Duplicate Scene View declarations are rejected");
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

        var gate = new InputGateProvider(input);
        Check(map.ActionDown(gate, fire) && gate.IsKeyDown(InputKey.Space),
            "An open InputGate preserves physical and mapped gameplay input");
        gate.IsBlocked = true;
        Check(!map.ActionDown(gate, fire) && map.Axis2D(gate, move) == Vector2D.Zero &&
              !gate.IsKeyDown(InputKey.Space) && gate.PointerCount == 0,
            "A blocked InputGate exposes one neutral frame across keys, actions, axes, and pointers");
        gate.IsBlocked = false;
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

    private static void TestLogicalInputRecordingAndPlayback()
    {
        Console.WriteLine("3b. Tick logical input recording and playback");
        var fire = new InputActionRef("player.fire");
        var dash = new InputActionRef("player.dash");
        var move = new InputAxis2DRef("player.move");
        double fixedDelta = 1d / 60d;
        InputMap map = new InputMapBuilder()
            .BindAction(fire, InputKey.Space)
            .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S)
            .BindAction(dash, InputKey.Shift)
            .Build();
        var physical = new MappedInputProbe();
        var recorder = new LogicalInputRecorder(initialFrameCapacity: 2);
        recorder.Prepare(map, fixedDelta);

        physical.Down.Add(InputKey.Space);
        physical.Pressed.Add(InputKey.Space);
        physical.Down.Add(InputKey.D);
        physical.Down.Add(InputKey.W);
        recorder.BeginStep(1, map, physical);

        physical.Down.Clear();
        physical.Pressed.Clear();
        physical.Released.Add(InputKey.Space);
        physical.Down.Add(InputKey.Shift);
        physical.Pressed.Add(InputKey.Shift);
        recorder.BeginStep(2, map, physical);

        LogicalInputRecording recording = recorder.Snapshot();
        Check(recording.FormatVersion == 1 &&
              recording.FixedDeltaSeconds == fixedDelta &&
              recording.FrameCount == 2 &&
              recording.FirstStepIndex == 1 &&
              recording.LastStepIndex == 2 &&
              recording.Actions.Span.SequenceEqual(
                  new[] { dash, fire }) &&
              recording.Axes2D.Span.SequenceEqual(new[] { move }),
            "Recording freezes a versioned, name-sorted logical schema and Tick range");

        var playback = new LogicalInputPlayback(recording, map);
        Check(!map.ActionDown(playback, fire) && map.Axis2D(playback, move) == Vector2D.Zero,
            "Playback exposes neutral logical input before the first simulation Tick");
        playback.BeginStep(1);
        Check(map.ActionDown(playback, fire) &&
              map.ActionPressed(playback, fire) &&
              !map.ActionReleased(playback, fire) &&
              !map.ActionDown(playback, dash) &&
              map.Axis2D(playback, move) == new Vector2D(1, -1),
            "Playback reproduces Action edges, held state, and Axis values for Tick one");
        playback.BeginStep(2);
        Check(!map.ActionDown(playback, fire) &&
              !map.ActionPressed(playback, fire) &&
              map.ActionReleased(playback, fire) &&
              map.ActionDown(playback, dash) &&
              map.ActionPressed(playback, dash) &&
              map.Axis2D(playback, move) == Vector2D.Zero &&
              playback.IsComplete,
            "Playback reproduces the next Tick and reports exact stream completion");

        CheckThrows<InvalidOperationException>(
            () => playback.BeginStep(3),
            "Playback fails when the simulation requests a missing Tick");
        CheckThrows<InvalidOperationException>(
            () => playback.IsKeyDown(InputKey.Space),
            "Replay mode rejects physical key queries instead of silently diverging");
        CheckThrows<InvalidOperationException>(
            () => _ = playback.PointerCount,
            "Replay mode rejects raw Pointer queries instead of silently diverging");
        CheckThrows<InvalidOperationException>(
            () => recorder.BeginStep(4, map, physical),
            "Recorder rejects a non-contiguous simulation Tick");

        InputMap equivalentMap = new InputMapBuilder()
            .BindAxis2D(move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down)
            .BindAction(dash, InputKey.Control)
            .BindAction(fire, InputKey.Enter)
            .Build();
        recording.ValidateAgainst(equivalentMap);
        Check(true,
            "Recordings depend on logical names and kinds rather than physical bindings/order");
        InputMap incompatibleMap = new InputMapBuilder()
            .BindAction(fire, InputKey.Space)
            .Build();
        CheckThrows<InvalidOperationException>(
            () => recording.ValidateAgainst(incompatibleMap),
            "Playback rejects an incompatible logical InputMap schema");

        int RunDeterministicSession()
        {
            var session = new LogicalInputPlayback(recording, equivalentMap);
            var random = new GameplayRandom(0xC0FFEEUL);
            int state = 0;
            for (ulong tick = 1; tick <= 2; tick++)
            {
                session.BeginStep(tick);
                if (equivalentMap.ActionPressed(session, fire))
                    state += random.Range(10, 100);
                if (equivalentMap.ActionPressed(session, dash))
                    state -= random.Range(1, 10);
                Vector2D axis = equivalentMap.Axis2D(session, move);
                state += (int)(axis.X * 100f) + (int)(axis.Y * 10f);
            }
            return state;
        }
        Check(RunDeterministicSession() == RunDeterministicSession(),
            "The same seed and logical Tick stream produce the same gameplay result");

        var fixedOptions = EngineWindowOptions.Default.WithFixedUpdateRate(60d);
        var replayRecordingSession = ReplaySession.Record(
            new ReplayIdentity("hosting-tests", "dev"));
        var replayRecordingPlan = GameApplication.Create(fixedOptions)
            .ConfigureInput(input => input.BindAction(fire, InputKey.Space))
            .UseReplayRecording(replayRecordingSession)
            .UseDefault2DRenderer()
            .ConfigureScene("ReplayRecord", _ => { })
            .BuildPlan();
        Check(ReferenceEquals(replayRecordingPlan.InputRecorder,
                  replayRecordingSession.InputRecorder) &&
              ReferenceEquals(replayRecordingPlan.StateRecorder,
                  replayRecordingSession.StateRecorder) &&
              replayRecordingPlan.InputPlayback is null &&
              replayRecordingPlan.StateVerifier is null,
            "Replay recording configures logical input and state hashing as one session");

        var sourceSession = ReplaySession.Record(
            new ReplayIdentity("hosting-tests", "dev"), 1);
        sourceSession.InputRecorder!.Prepare(map, fixedDelta);
        sourceSession.InputRecorder.BeginStep(1, map, physical);
        sourceSession.StateRecorder!.Prepare(fixedDelta);
        var replayScene = new SceneAggregate("Replay");
        replayScene.PerformStep(fixedDelta);
        sourceSession.StateRecorder.Capture(replayScene.CaptureGameplayState());
        ReplaySession replayPlaybackSession = ReplaySession.Play(
            sourceSession.Snapshot(),
            new ReplayIdentity("hosting-tests", "dev"));
        var replaySessionPlan = GameApplication.Create(fixedOptions)
            .ConfigureInput(input => input
                .BindAction(fire, InputKey.Enter)
                .BindAction(dash, InputKey.Control)
                .BindAxis2D(move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down))
            .UseReplayPlayback(replayPlaybackSession)
            .UseDefault2DRenderer()
            .ConfigureScene("Replay", _ => { })
            .BuildPlan();
        Check(ReferenceEquals(replaySessionPlan.InputPlayback,
                  replayPlaybackSession.Bundle!.Input) &&
              ReferenceEquals(replaySessionPlan.StateVerifier!.Recording,
                  replayPlaybackSession.Bundle.GameplayState) &&
              replaySessionPlan.CloseOnReplayCompletion,
            "Replay playback validates both streams and closes after the final verified Tick");

        var recordingPlan = GameApplication.Create(fixedOptions)
            .ConfigureInput(input => input.BindAction(fire, InputKey.Space))
            .RecordLogicalInput(new LogicalInputRecorder())
            .UseDefault2DRenderer()
            .ConfigureScene("Record", _ => { })
            .BuildPlan();
        Check(recordingPlan.InputRecorder is not null && recordingPlan.InputPlayback is null,
            "Hosting plan freezes an explicit logical input recording mode");
        var playbackPlan = GameApplication.Create(fixedOptions)
            .ConfigureInput(input => input
                .BindAction(fire, InputKey.Enter)
                .BindAction(dash, InputKey.Control)
                .BindAxis2D(move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down))
            .ReplayLogicalInput(recording)
            .UseDefault2DRenderer()
            .ConfigureScene("Replay", _ => { })
            .BuildPlan();
        Check(playbackPlan.InputRecorder is null &&
              ReferenceEquals(playbackPlan.InputPlayback, recording),
            "Hosting plan accepts playback across equivalent physical bindings");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .ConfigureInput(input => input.BindAction(fire, InputKey.Space))
                .RecordLogicalInput(new LogicalInputRecorder())
                .UseDefault2DRenderer()
                .ConfigureScene("NoFixedDelta", _ => { })
                .BuildPlan(),
            "Hosting requires a fixed delta before recording logical input");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(
                    EngineWindowOptions.Default.WithFixedUpdateRate(30d))
                .ConfigureInput(input => input
                    .BindAction(fire, InputKey.Enter)
                    .BindAction(dash, InputKey.Control)
                    .BindAxis2D(move, InputKey.Left, InputKey.Right, InputKey.Up, InputKey.Down))
                .ReplayLogicalInput(recording)
                .UseDefault2DRenderer()
                .ConfigureScene("WrongDelta", _ => { })
                .BuildPlan(),
            "Hosting rejects playback under a different fixed delta");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .RecordLogicalInput(new LogicalInputRecorder())
                .UseDefault2DRenderer()
                .ConfigureScene("NoMap", _ => { })
                .BuildPlan(),
            "Hosting requires a configured logical InputMap before recording");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .RecordLogicalInput(new LogicalInputRecorder())
                .ReplayLogicalInput(recording),
            "Hosting rejects simultaneous recording and playback modes");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .ConfigureInput(input => input
                    .BindAction(fire, InputKey.Space)
                    .BindAction(dash, InputKey.Shift)
                    .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S))
                .RecordLogicalInput(recorder)
                .UseDefault2DRenderer()
                .ConfigureScene("UsedRecorder", _ => { })
                .BuildPlan(),
            "Hosting rejects a Recorder that already owns captured frames");

        var emptyRecorder = new LogicalInputRecorder();
        emptyRecorder.Prepare(map, fixedDelta);
        LogicalInputRecording emptyRecording = emptyRecorder.Snapshot();
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .ConfigureInput(input => input
                    .BindAction(fire, InputKey.Space)
                    .BindAction(dash, InputKey.Shift)
                    .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S))
                .ReplayLogicalInput(emptyRecording)
                .UseDefault2DRenderer()
                .ConfigureScene("EmptyReplay", _ => { })
                .BuildPlan(),
            "Hosting rejects an empty playback stream");

        var partialRecorder = new LogicalInputRecorder();
        partialRecorder.BeginStep(42, map, physical);
        LogicalInputRecording partialRecording = partialRecorder.Snapshot();
        Check(partialRecording.FirstStepIndex == 42,
            "Manual recording supports a positive partial-session first Tick");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .ConfigureInput(input => input
                    .BindAction(fire, InputKey.Space)
                    .BindAction(dash, InputKey.Shift)
                    .BindAxis2D(move, InputKey.A, InputKey.D, InputKey.W, InputKey.S))
                .ReplayLogicalInput(partialRecording)
                .UseDefault2DRenderer()
                .ConfigureScene("PartialReplay", _ => { })
                .BuildPlan(),
            "Hosting full-session playback must begin at simulation Tick one");

        var queryPlayback = new LogicalInputPlayback(recording, map);
        queryPlayback.BeginStep(1);
        for (int i = 0; i < 64; i++)
        {
            _ = map.ActionDown(queryPlayback, fire);
            _ = map.ActionPressed(queryPlayback, fire);
            _ = map.ActionReleased(queryPlayback, fire);
            _ = map.Axis2D(queryPlayback, move);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            _ = map.ActionDown(queryPlayback, fire);
            _ = map.ActionPressed(queryPlayback, fire);
            _ = map.ActionReleased(queryPlayback, fire);
            _ = map.Axis2D(queryPlayback, move);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Playback logical queries remain allocation-free ({allocated:N0} B)");
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

    private static void TestDeterministicSimulationPrimitives()
    {
        Console.WriteLine("3e. Deterministic simulation clock and random");
        EngineWindowOptions fixedOptions = EngineWindowOptions.Default.WithFixedUpdateRate(60d);
        Check(fixedOptions.UpdatesPerSecond == 60d &&
              fixedOptions.FixedDeltaTime == 1d / 60d,
            "Fixed update configuration couples native UPS and logical delta");
        CheckThrows<ArgumentOutOfRangeException>(
            () => EngineWindowOptions.Default.WithFixedUpdateRate(0d),
            "Fixed update rate must be finite and positive");
        CheckThrows<ArgumentOutOfRangeException>(
            () => EngineWindowOptions.Default.WithFixedUpdateRate(double.Epsilon),
            "Fixed update rate must produce a representable delta");

        var clockProbe = new SimulationClockProbe();
        var scene = new SceneAggregate("SimulationClock");
        scene.Add(clockProbe);
        Check(scene.Clock.StepIndex == 0 && scene.Clock.UnscaledElapsedSeconds == 0d,
            "Simulation clock starts before Tick zero has advanced");

        scene.PerformStep(0.25d);
        Check(scene.Clock.StepIndex == 1 && scene.Clock.UnscaledDeltaSeconds == 0.25d &&
              scene.Clock.GameplayDeltaSeconds == 0.25d &&
              scene.Clock.UnscaledElapsedSeconds == 0.25d &&
              scene.Clock.GameplayElapsedSeconds == 0.25d &&
              clockProbe.Observed == scene.Clock.Current,
            "Every instance observes the same clock snapshot for one Step");

        scene.Time.TimeScale = 0.5d;
        scene.PerformStep(0.2d);
        Check(scene.Clock.StepIndex == 2 && scene.Clock.UnscaledElapsedSeconds == 0.45d &&
              Math.Abs(scene.Clock.GameplayElapsedSeconds - 0.35d) < 0.000001d &&
              scene.Clock.GameplayDeltaSeconds == 0.1d && scene.Clock.TimeScale == 0.5d,
            "Clock accumulates scaled Gameplay time from the existing time controller");

        var pause = new GameplayPauseKey("simulation-clock-test");
        scene.Time.Pause(pause);
        scene.PerformStep(0.4d);
        Check(scene.Clock.StepIndex == 3 && scene.Clock.IsPaused &&
              Math.Abs(scene.Clock.UnscaledElapsedSeconds - 0.85d) < 0.000001d &&
              Math.Abs(scene.Clock.GameplayElapsedSeconds - 0.35d) < 0.000001d &&
              clockProbe.Observed.StepIndex == 2,
            "Paused Steps advance Tick and Unscaled time while Gameplay instances remain frozen");

        scene.TransitionTo("ClockContinues");
        Check(scene.Clock.StepIndex == 3 &&
              Math.Abs(scene.Clock.UnscaledElapsedSeconds - 0.85d) < 0.000001d,
            "Scene transitions preserve the application simulation timeline");
        var exhaustedClockScene = new SceneAggregate("ClockOverflow");
        exhaustedClockScene.PerformStep(double.MaxValue);
        CheckThrows<InvalidOperationException>(
            () => exhaustedClockScene.PerformStep(double.MaxValue),
            "Simulation clock rejects elapsed-time overflow");

        var random = new GameplayRandom(0xA57E201DUL);
        uint[] expected =
        [
            3639831199U,
            2639829579U,
            1201333440U,
            179796295U,
            4267463458U,
            1499256909U
        ];
        uint[] actual = new uint[expected.Length];
        for (int i = 0; i < actual.Length; i++) actual[i] = random.NextUInt();
        Check(actual.SequenceEqual(expected) && GameplayRandom.AlgorithmVersion == 1,
            "PCG32 exposes one versioned cross-runtime golden bit sequence");

        random.Reset(0xA57E201DUL);
        Check(random.NextUInt() == expected[0],
            "Reset reproduces the stream from its seed");
        GameplayRandomState saved = random.CaptureState();
        uint afterSave = random.NextUInt();
        random.RestoreState(saved);
        Check(random.NextUInt() == afterSave,
            "Captured random state resumes at the exact next value");

        var sameSeedA = new GameplayRandom(42UL);
        var sameSeedB = new GameplayRandom(42UL);
        var differentSeed = new GameplayRandom(43UL);
        Check(sameSeedA.NextUInt() == sameSeedB.NextUInt() &&
              sameSeedA.NextUInt() == sameSeedB.NextUInt() &&
              differentSeed.NextUInt() != new GameplayRandom(42UL).NextUInt(),
            "Owner-local streams match by seed and separate across seeds");

        var ranges = new GameplayRandom(7UL);
        bool rangesValid = true;
        for (int i = 0; i < 1_024; i++)
        {
            int integer = ranges.Range(-9, 13);
            float scalar = ranges.Range(-2.5f, 4.75f);
            rangesValid &= integer >= -9 && integer < 13 &&
                           scalar >= -2.5f && scalar < 4.75f;
        }
        Check(rangesValid && !ranges.Chance(0f) && ranges.Chance(1f),
            "Integer, float, and probability helpers preserve their documented bounds");

        Vector2D direction = ranges.Direction2D();
        Vector2D point = ranges.InsideCircle(5f);
        Check(Math.Abs(direction.Length() - 1f) < 0.00001f && point.Length() <= 5.00001f,
            "Direction and circle helpers produce valid gameplay geometry");

        int[] choices = [10, 20, 30, 40];
        int chosen = ranges.Choose<int>(choices);
        int[] shuffledA = [1, 2, 3, 4, 5];
        int[] shuffledB = [1, 2, 3, 4, 5];
        var shuffleA = new GameplayRandom(99UL);
        var shuffleB = new GameplayRandom(99UL);
        shuffleA.Shuffle<int>(shuffledA);
        shuffleB.Shuffle<int>(shuffledB);
        Check(choices.Contains(chosen) && shuffledA.SequenceEqual(shuffledB) &&
              shuffledA.Order().SequenceEqual(new[] { 1, 2, 3, 4, 5 }),
            "Choose and Fisher-Yates Shuffle are deterministic and preserve values");

        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.NextInt(0),
            "Random integer maximum must be positive");
        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.Range(3, 3),
            "Random integer ranges must be non-empty");
        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.Range(float.NaN, 1f),
            "Random float bounds must be finite");
        GameplayRandomState beforeInvalidRange = ranges.CaptureState();
        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.Range(-float.MaxValue, float.MaxValue),
            "Random float range rejects an overflowing width");
        Check(ranges.CaptureState() == beforeInvalidRange,
            "Rejected random ranges do not advance the stream");
        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.Chance(1.1f),
            "Random probability must remain within zero and one");
        CheckThrows<ArgumentOutOfRangeException>(
            () => ranges.InsideCircle(-1f),
            "Random circle radius cannot be negative");
        CheckThrows<ArgumentException>(
            () => ranges.Choose<int>(ReadOnlySpan<int>.Empty),
            "Random choice rejects an empty span");

        var allocationRandom = new GameplayRandom(1234UL);
        int[] allocationValues = [1, 2, 3, 4, 5, 6, 7, 8];
        for (int i = 0; i < 64; i++)
        {
            _ = allocationRandom.NextUInt();
            _ = allocationRandom.Range(-100, 100);
            _ = allocationRandom.Range(-1f, 1f);
            _ = allocationRandom.Choose<int>(allocationValues);
            allocationRandom.Shuffle<int>(allocationValues);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            _ = allocationRandom.NextUInt();
            _ = allocationRandom.Range(-100, 100);
            _ = allocationRandom.Range(-1f, 1f);
            _ = allocationRandom.Choose<int>(allocationValues);
            allocationRandom.Shuffle<int>(allocationValues);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Deterministic random helpers remain allocation-free ({allocated:N0} B)");
    }

    private static void TestGameplayStateHashing()
    {
        Console.WriteLine("3f. Gameplay state hash and divergence diagnostics");
        var goldenWriter = new GameplayStateWriter();
        goldenWriter.Write("score", 42);
        goldenWriter.Write("alive", true);
        goldenWriter.Write("position", new Vector2D(1.5f, -2.25f));
        Check(GameplayStateWriter.AlgorithmVersion == 1 &&
              goldenWriter.Hash == 0x84AF633F1EED9740UL,
            "State writer exposes one versioned cross-runtime golden hash");

        var reorderedWriter = new GameplayStateWriter();
        reorderedWriter.Write("alive", true);
        reorderedWriter.Write("score", 42);
        reorderedWriter.Write("position", new Vector2D(1.5f, -2.25f));
        Check(reorderedWriter.Hash != goldenWriter.Hash,
            "Field names, types, values, and declaration order form the hash schema");

        const double fixedDelta = 1d / 60d;
        (SceneAggregate first, StateHashProbe firstProbe) = CreateStateScene(reverseMetadata: false);
        (SceneAggregate second, StateHashProbe secondProbe) = CreateStateScene(reverseMetadata: true);
        first.PerformStep(fixedDelta);
        second.PerformStep(fixedDelta);
        GameplayStateSnapshot firstSnapshot = first.CaptureGameplayState();
        GameplayStateSnapshot secondSnapshot = second.CaptureGameplayState();
        Check(firstSnapshot.Hash == secondSnapshot.Hash &&
              firstSnapshot.Contributors.SequenceEqual(secondSnapshot.Contributors) &&
              firstProbe.Id != secondProbe.Id,
            "Independent Scenes hash equally without including random InstanceId or metadata order");
        Check(firstSnapshot.Contributors.Count == 2 &&
              firstSnapshot.Contributors[0].Kind == "Scene:StateHash" &&
              firstSnapshot.Contributors[1].Sequence == 0 &&
              firstSnapshot.Contributors[1].Kind == nameof(StateHashProbe),
            "Snapshot separates Scene and stable-sequence Instance contributors");

        var recorder = new GameplayStateRecorder(initialCapacity: 2);
        recorder.Prepare(fixedDelta);
        recorder.Capture(firstSnapshot);
        first.PerformStep(fixedDelta);
        recorder.Capture(first.CaptureGameplayState());
        GameplayStateRecording recording = recorder.Snapshot();
        Check(recording.FormatVersion == 1 &&
              recording.FixedDeltaSeconds == fixedDelta &&
              recording.SnapshotCount == 2 &&
              recording.FirstStepIndex == 1 &&
              recording.LastStepIndex == 2,
            "State recorder freezes a versioned fixed-delta Tick trace");

        var verifier = new GameplayStateVerifier(recording);
        Check(verifier.Verify(secondSnapshot),
            "Verifier accepts the matching first Tick");
        secondProbe.Score++;
        second.PerformStep(fixedDelta);
        GameplayStateSnapshot divergentSnapshot = second.CaptureGameplayState();
        Check(!verifier.Verify(divergentSnapshot) &&
              verifier.FirstDivergence is { StepIndex: 2 } divergence &&
              divergence.ExpectedHash != divergence.ActualHash &&
              divergence.ExpectedContributor is { Sequence: 0 } &&
              divergence.ActualContributor is { Sequence: 0, Kind: nameof(StateHashProbe) },
            "Verifier retains the first divergent Tick and Instance contributor");
        CheckThrows<GameplayStateDivergenceException>(
            () => throw new GameplayStateDivergenceException(verifier.FirstDivergence!),
            "Divergence exception carries the structured first-difference diagnostic");

        var fixedOptions = EngineWindowOptions.Default.WithFixedUpdateRate(60d);
        var stateRecorder = new GameplayStateRecorder();
        var recordPlan = GameApplication.Create(fixedOptions)
            .RecordGameplayState(stateRecorder)
            .UseDefault2DRenderer()
            .ConfigureScene("StateRecord", _ => { })
            .BuildPlan();
        Check(ReferenceEquals(recordPlan.StateRecorder, stateRecorder) &&
              recordPlan.StateVerifier is null,
            "Hosting plan freezes an explicit gameplay state recording mode");
        var verifyPlan = GameApplication.Create(fixedOptions)
            .VerifyGameplayState(new GameplayStateVerifier(recording))
            .UseDefault2DRenderer()
            .ConfigureScene("StateVerify", _ => { })
            .BuildPlan();
        Check(verifyPlan.StateRecorder is null && verifyPlan.StateVerifier is not null,
            "Hosting plan freezes first-divergence verification before window creation");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create()
                .RecordGameplayState(new GameplayStateRecorder())
                .UseDefault2DRenderer()
                .ConfigureScene("NoFixedState", _ => { })
                .BuildPlan(),
            "Hosting state diagnostics require a fixed delta");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(
                    EngineWindowOptions.Default.WithFixedUpdateRate(30d))
                .VerifyGameplayState(new GameplayStateVerifier(recording))
                .UseDefault2DRenderer()
                .ConfigureScene("WrongStateDelta", _ => { })
                .BuildPlan(),
            "Hosting rejects state baselines recorded with a different fixed delta");
        CheckThrows<InvalidOperationException>(
            () => GameApplication.Create(fixedOptions)
                .RecordGameplayState(new GameplayStateRecorder())
                .VerifyGameplayState(new GameplayStateVerifier(recording)),
            "Hosting rejects simultaneous state recording and verification modes");

        ulong allocationHash = 0;
        for (int i = 0; i < 64; i++)
        {
            var writer = new GameplayStateWriter();
            writer.Write("score", i);
            writer.Write("position", new Vector2D(i, -i));
            allocationHash ^= writer.Hash;
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            var writer = new GameplayStateWriter();
            writer.Write("score", i);
            writer.Write("position", new Vector2D(i, -i));
            allocationHash ^= writer.Hash;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0 && allocationHash != 0,
            $"State writer remains allocation-free ({allocated:N0} B)");

        static (SceneAggregate Scene, StateHashProbe Probe) CreateStateScene(bool reverseMetadata)
        {
            var scene = new SceneAggregate("StateHash");
            var probe = new StateHashProbe
            {
                Position = new Vector2D(12f, 34f),
                Collider = CollisionShape2D.Circle(5f)
            };
            if (reverseMetadata)
            {
                probe.AddTag(new GameplayTag("actor.player"));
                probe.AddTag(new GameplayTag("combat.damageable"));
                probe.SetAlarm(new AlarmId("fire"), 2d);
                probe.SetAlarm(new AlarmId("spawn"), 1d);
            }
            else
            {
                probe.AddTag(new GameplayTag("combat.damageable"));
                probe.AddTag(new GameplayTag("actor.player"));
                probe.SetAlarm(new AlarmId("spawn"), 1d);
                probe.SetAlarm(new AlarmId("fire"), 2d);
            }
            scene.Add(probe);
            return (scene, probe);
        }
    }

    private static void TestInstanceReferences()
    {
        Console.WriteLine("3d. Strongly typed instance references");
        var scene = new SceneAggregate("InstanceReferences");
        var target = new HostingPrefabProbe(new Vector2D(4f, 5f));
        scene.Add(target);
        InstanceRef<HostingPrefabProbe> reference = target.ToInstanceRef();

        Check(!reference.IsEmpty && reference.Id == target.Id &&
              ReferenceEquals(scene.Resolve(reference), target),
            "Instance creates a weak strongly typed reference that resolves in its Scene");
        Check(scene.Resolve(InstanceRef<HostingPrefabProbe>.Empty) is null &&
              scene.Resolve(default(InstanceRef<HostingPrefabProbe>)) is null,
            "Empty and default references resolve safely to null");

        var forgedType = new InstanceRef<CountingOwner>(target.Id);
        scene.Destroy(forgedType);
        Check(scene.Resolve(forgedType) is null && ReferenceEquals(scene.Resolve(reference), target),
            "A forged mismatched generic type cannot resolve or destroy the target");

        target.SetActive(false, scene.RaiseEvent);
        Check(ReferenceEquals(scene.Resolve(reference), target),
            "Inactive committed instances remain addressable");

        var frameProbe = new InstanceRefLifecycleProbe();
        scene.Add(frameProbe);
        scene.PerformStep(1d / 60d);
        Check(!frameProbe.ResolvedBeforeSpawnCommit &&
              scene.Resolve(frameProbe.SpawnedReference) is not null,
            "Queued Spawn references become visible only after End Step commit");
        scene.PerformStep(1d / 60d);
        Check(frameProbe.ResolvedAfterDestroyRequest &&
              scene.Resolve(frameProbe.SpawnedReference) is null,
            "Queued Destroy references remain visible during the Step and expire after commit");

        var transitionScene = new SceneAggregate("BeforeTransition");
        var persistent = new HostingPrefabProbe(Vector2D.Zero) { IsPersistent = true };
        var transient = new HostingPrefabProbe(Vector2D.Zero);
        InstanceRef<HostingPrefabProbe> persistentRef = persistent.ToInstanceRef();
        InstanceRef<HostingPrefabProbe> transientRef = transient.ToInstanceRef();
        transitionScene.Add(persistent);
        transitionScene.Add(transient);
        transitionScene.Start();
        transitionScene.TransitionTo("AfterTransition");
        Check(ReferenceEquals(transitionScene.Resolve(persistentRef), persistent) &&
              transitionScene.Resolve(transientRef) is null,
            "Scene transitions retain persistent references and invalidate transient ones");

        var replacement = new HostingPrefabProbe(Vector2D.Zero);
        transitionScene.Add(replacement);
        Check(transitionScene.Resolve(transientRef) is null &&
              replacement.ToInstanceRef() != transientRef,
            "A removed reference never aliases a later instance of the same type");

        for (int i = 0; i < 64; i++)
            _ = transitionScene.Resolve(persistentRef);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            _ = transitionScene.Resolve(persistentRef);
            _ = persistent.ToInstanceRef();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Reference creation and dictionary resolution remain allocation-free ({allocated:N0} B)");
    }

    private static void TestGameplayHealth()
    {
        Console.WriteLine("3c. Gameplay health and damage");
        var health = new GameplayHealth(10f);
        Check(health.CurrentHealth == 10f && health.MaximumHealth == 10f &&
              health.Normalized == 1f && health.IsAlive && health.IsFull,
            "Health starts full by default");

        GameplayHealthChange damage = health.ApplyDamage(3f);
        Check(damage.PreviousHealth == 10f && damage.CurrentHealth == 7f &&
              damage.MaximumHealth == 10f && damage.Delta == -3f &&
              damage.AppliedAmount == 3f && damage.IsDamage && !damage.BecameDepleted &&
              health.Normalized == 0.7f,
            "Damage reports the exact clamped value change");

        damage = health.ApplyDamage(20f);
        Check(damage.AppliedAmount == 7f && damage.BecameDepleted &&
              health.IsDepleted && health.CurrentHealth == 0f,
            "Overkill damage clamps at zero and reports one depletion transition");
        damage = health.ApplyDamage(1f);
        Check(!damage.Changed && !damage.BecameDepleted && damage.AppliedAmount == 0f,
            "Repeated damage on depleted health has no duplicate transition");

        GameplayHealthChange healing = health.Heal(4f);
        Check(healing.IsHealing && healing.BecameAlive && !healing.ReachedFull &&
              health.CurrentHealth == 4f,
            "Healing can explicitly revive depleted health");
        healing = health.Heal(20f);
        Check(healing.AppliedAmount == 6f && healing.ReachedFull && health.IsFull,
            "Overhealing clamps at maximum and reports reaching full health");

        health.ApplyDamage(1f);
        GameplayHealthChange reset = health.Reset();
        Check(reset.ReachedFull && health.CurrentHealth == health.MaximumHealth,
            "Reset restores full health and returns its change snapshot");

        var depleted = new GameplayHealth(5f, 0f);
        Check(depleted.IsDepleted && depleted.Normalized == 0f,
            "Explicit initial health supports pre-depleted state");

        CheckThrows<ArgumentOutOfRangeException>(
            () => new GameplayHealth(0f),
            "Health rejects non-positive maximum values");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new GameplayHealth(10f, 11f),
            "Health rejects initial values above maximum");
        CheckThrows<ArgumentOutOfRangeException>(
            () => health.ApplyDamage(float.NaN),
            "Damage rejects non-finite amounts");
        CheckThrows<ArgumentOutOfRangeException>(
            () => health.Heal(-1f),
            "Healing rejects negative amounts");

        var allocationHealth = new GameplayHealth(100f);
        for (int i = 0; i < 64; i++)
        {
            allocationHealth.ApplyDamage(1f);
            allocationHealth.Heal(1f);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            allocationHealth.ApplyDamage(1f);
            allocationHealth.Heal(1f);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Health mutations and result snapshots remain allocation-free ({allocated:N0} B)");
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

    private static void TestSpawnSequences()
    {
        Console.WriteLine("3h. Deterministic Spawn/Wave authoring");
        SpawnSequence finite = new SpawnSequenceBuilder()
            .Delay(0.5d)
            .Wave(count: 3, intervalSeconds: 0.25d)
            .Delay(0.5d)
            .Wave(count: 2, intervalSeconds: 0d)
            .Build();
        var emissions = new List<SpawnEmission>();
        var player = new SpawnSequencePlayer(finite);
        SpawnEmissionHandler record = (in SpawnEmission emission) => emissions.Add(emission);

        Check(player.Update(0.49d, 0, record) == 0 && emissions.Count == 0,
            "Initial delay holds a finite wave until its deterministic boundary");
        Check(player.Update(0.01d, 0, record) == 1 &&
              emissions[0] == new SpawnEmission(0, 0, 0, 0),
            "Entering a wave makes its first item immediately ready");
        Check(player.Update(0.5d, 0, record) == 2 && emissions.Count == 3 &&
              emissions[2] == new SpawnEmission(0, 0, 2, 2) && !player.IsCompleted,
            "A large Step deterministically carries time across every due wave item");
        Check(player.Update(0.5d, 0, record) == 2 && player.IsCompleted &&
              emissions[3].WaveIndex == 1 && emissions[3].ItemIndex == 0 &&
              emissions[4].WaveIndex == 1 && emissions[4].ItemIndex == 1,
            "Finite multi-wave sequences explicitly complete after their last item");

        SpawnSequence loop = new SpawnSequenceBuilder()
            .Wave(count: 1, intervalSeconds: 0.1d)
            .Build(SpawnSequenceRepeat.Loop, maximumConcurrent: 2);
        var loopSink = new SpawnCountingSink();
        var looping = new SpawnSequencePlayer(loop);
        Check(looping.Update(1d, 2, loopSink.Emit) == 0 && looping.IsWaitingForCapacity,
            "Maximum-concurrent gate blocks a ready emission without advancing it");
        Check(looping.Update(1d, 1, loopSink.Emit) == 1 && looping.IsWaitingForCapacity &&
              loopSink.Count == 1,
            "Queued emissions count toward the gate before Scene mutation commit");
        Check(looping.Update(0d, 1, loopSink.Emit) == 1 && loopSink.Count == 2,
            "A gated emission resumes without accumulating a catch-up burst");
        looping.Complete();
        Check(looping.IsCompleted && looping.Update(10d, 0, loopSink.Emit) == 0,
            "Explicit completion stops a looping sequence");
        looping.Restart();
        Check(!looping.IsCompleted && looping.TotalEmissions == 0 &&
              looping.Update(0d, 0, loopSink.Emit) == 1,
            "Restart rewinds timeline and deterministic emission counters");

        var stateSource = new SpawnSequencePlayer(loop);
        stateSource.Update(0.05d, 0, loopSink.Emit);
        SpawnSequencePlayerState saved = stateSource.CaptureState();
        var restored = new SpawnSequencePlayer(loop);
        restored.RestoreState(saved);
        Check(restored.CaptureState() == saved,
            "Player state can be captured and restored for deterministic replay diagnostics");
        SpawnSequencePlayerState finiteCompleted = player.CaptureState();
        CheckThrows<ArgumentException>(
            () => player.RestoreState(finiteCompleted with { WaveIndex = 0 }),
            "State restore rejects a Wave index that conflicts with the authored segment");
        CheckThrows<ArgumentException>(
            () => restored.RestoreState(saved with { WaitingAtLoopBoundary = false }),
            "State restore rejects an impossible loop-boundary flag");

        CheckThrows<InvalidOperationException>(
            () => new SpawnSequenceBuilder()
                .Wave(1, 0d)
                .Build(SpawnSequenceRepeat.Loop),
            "Looping sequences reject zero-duration infinite timelines");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new SpawnSequenceBuilder().Wave(0, 1d),
            "Wave authoring rejects empty finite waves");

        SpawnSequence delayed = new SpawnSequenceBuilder()
            .Delay(0.2d)
            .Wave(1, 0d)
            .Build();
        var gameplayOwner = new SpawnSequenceProbe(delayed);
        var unscaledOwner = new SpawnSequenceProbe(delayed)
            { TimeMode = InstanceTimeMode.Unscaled };
        var scene = new SceneAggregate("SpawnSequenceTimeDomains");
        scene.Add(gameplayOwner);
        scene.Add(unscaledOwner);
        scene.PerformStep(0.1d);
        var pause = new GameplayPauseKey("spawn-sequence-test");
        scene.Time.Pause(pause);
        scene.PerformStep(0.1d);
        Check(gameplayOwner.EmissionCount == 0 && unscaledOwner.EmissionCount == 1,
            "Owner-driven sequences inherit Gameplay pause and Unscaled time semantics");
        scene.Time.Resume(pause);
        gameplayOwner.SetActive(false, _ => { });
        scene.PerformStep(0.1d);
        Check(gameplayOwner.EmissionCount == 0,
            "Inactive owners do not advance their Spawn sequence");
        gameplayOwner.SetActive(true, _ => { });
        scene.PerformStep(0.1d);
        Check(gameplayOwner.EmissionCount == 1,
            "Reactivated owners continue from their preserved timeline position");

        var allocationSink = new SpawnCountingSink();
        SpawnEmissionHandler allocationEmit = allocationSink.Emit;
        SpawnSequence allocationPlan = new SpawnSequenceBuilder()
            .Wave(1, 0.001d)
            .Build(SpawnSequenceRepeat.Loop);
        var allocationPlayer = new SpawnSequencePlayer(allocationPlan);
        for (int i = 0; i < 64; i++)
            allocationPlayer.Update(0.001d, 0, allocationEmit);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
            allocationPlayer.Update(0.001d, 0, allocationEmit);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocated == 0,
            $"Warmed Spawn sequence updates remain allocation-free ({allocated:N0} B)");
    }

    private static void TestGameplaySignals()
    {
        Console.WriteLine("3g. Scene-scoped strongly typed gameplay signals");
        var order = new List<string>();
        var scene = new SceneAggregate("Signals");
        var publisher = scene.Add(new SignalPublisher());
        var first = scene.Add(new SignalOrderProbe("first", order));
        var second = scene.Add(new SignalOrderProbe("second", order));
        publisher.Emit(new PrimarySignal(1));
        publisher.Emit(new SecondarySignal(9));
        publisher.Emit(new PrimarySignal(3));
        Check(scene.PendingGameplaySignalCount == 3 &&
              first.SignalHandlerCount == 2 && second.SignalHandlerCount == 2,
            "Publish queues typed value payloads and construction-time handlers");
        scene.PerformStep(1d / 60d);
        Check(order.SequenceEqual(new[]
            {
                "first.primary.1", "second.primary.1",
                "first.secondary.9", "second.secondary.9",
                "first.primary.3", "second.primary.3"
            }) && scene.PendingGameplaySignalCount == 0,
            "Dispatch preserves publication order then stable Scene subscription order");

        var nestedOrder = new List<string>();
        var nestedScene = new SceneAggregate("NestedSignals");
        var nestedPublisher = nestedScene.Add(new SignalPublisher());
        nestedScene.Add(new SignalOrderProbe("republisher", nestedOrder, republish: true));
        nestedScene.Add(new SignalOrderProbe("observer", nestedOrder));
        nestedPublisher.Emit(new PrimarySignal(1));
        nestedScene.PerformStep(1d / 60d);
        Check(nestedOrder.SequenceEqual(new[]
              { "republisher.primary.1", "observer.primary.1" }) &&
              nestedScene.PendingGameplaySignalCount == 1,
            "Signals published by handlers are deferred instead of recursively dispatched");
        nestedScene.PerformStep(1d / 60d);
        Check(nestedOrder.SequenceEqual(new[]
            {
                "republisher.primary.1", "observer.primary.1",
                "republisher.primary.2", "observer.primary.2"
            }),
            "Deferred nested signals arrive on the next Tick");

        var pausedOrder = new List<string>();
        var pausedScene = new SceneAggregate("PausedSignals");
        var pausedPublisher = pausedScene.Add(new SignalPublisher());
        var gameplayListener = pausedScene.Add(
            new SignalOrderProbe("gameplay", pausedOrder, primaryOnly: true));
        var unscaledListener = pausedScene.Add(
            new SignalOrderProbe("unscaled", pausedOrder, primaryOnly: true)
            { TimeMode = InstanceTimeMode.Unscaled });
        gameplayListener.SetActive(false, _ => { });
        pausedPublisher.Emit(new PrimarySignal(4));
        pausedScene.Time.Pause(new GameplayPauseKey("signal-test"));
        pausedScene.PerformStep(1d / 60d);
        Check(pausedOrder.SequenceEqual(new[] { "unscaled.primary.4" }),
            "Inactive and paused Gameplay handlers are skipped while Unscaled handlers receive");

        var removedOrder = new List<string>();
        var removedScene = new SceneAggregate("RemovedSignals");
        var removedPublisher = removedScene.Add(new SignalPublisher());
        var removedListener = removedScene.Add(
            new SignalOrderProbe("removed", removedOrder, primaryOnly: true));
        removedPublisher.Emit(new PrimarySignal(5));
        removedScene.Destroy(removedListener.Id);
        removedScene.PerformStep(1d / 60d);
        Check(removedOrder.Count == 0,
            "Destroy automatically detaches a handler before queued delivery");

        var mutationScene = new SceneAggregate("SignalMutations");
        var mutationPublisher = mutationScene.Add(new SignalPublisher());
        mutationScene.Add(new SignalSpawningProbe());
        mutationPublisher.Emit(new PrimarySignal(1));
        mutationScene.PerformStep(1d / 60d);
        Check(mutationScene.CountInstances<SignalSpawnedProbe>() == 1,
            "Signal handlers can join Spawn requests to the current safe mutation commit");

        var failureScene = new SceneAggregate("SignalFailure");
        var failurePublisher = failureScene.Add(new SignalPublisher());
        var failingHandler = failureScene.Add(new FailingSignalProbe());
        failurePublisher.Emit(new PrimarySignal(7));
        try
        {
            failureScene.PerformStep(1d / 60d);
            Check(false, "Signal handler failures expose structured publisher/receiver context");
        }
        catch (GameplaySignalDispatchException exception)
        {
            Check(exception.SignalType == typeof(PrimarySignal) &&
                  exception.PublisherId == failurePublisher.Id &&
                  exception.HandlerId == failingHandler.Id &&
                  exception.InnerException is InvalidOperationException,
                "Signal handler failures expose structured publisher/receiver context");
        }

        CheckThrows<InvalidOperationException>(
            () => _ = new MissingSignalInterfaceProbe(),
            "Listening requires the matching strongly typed handler interface");
        CheckThrows<InvalidOperationException>(
            () => _ = new DuplicateSignalListenerProbe(),
            "One Instance cannot accidentally register the same signal type twice");
        CheckThrows<InvalidOperationException>(
            first.ListenAfterAttach,
            "Signal handler declarations freeze when an Instance enters a Scene");

        var allocationScene = new SceneAggregate("SignalAllocation");
        var allocationPublisher = allocationScene.Add(new SignalPublisher());
        var allocationListener = allocationScene.Add(new CountingSignalProbe());
        for (int i = 0; i < 64; i++)
        {
            allocationPublisher.Emit(new PrimarySignal(i));
            allocationScene.PerformStep(1d / 60d);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            allocationPublisher.Emit(new PrimarySignal(i));
            allocationScene.PerformStep(1d / 60d);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Check(allocationListener.Count == 1_088 && allocated == 0,
            $"Warmed signal publish and dispatch remain allocation-free ({allocated:N0} B)");
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

        var mainPackage = new ContentPackageRef("scene.main", "Main/assets.json");
        var catalogPlan = GameApplication.Create()
            .UseDefault2DRenderer(renderer => renderer.UseContentCatalog("Compiled"))
            .AddScene(main, mainPackage, _ => { })
            .AddScene(gameOver, _ => { })
            .BuildPlan();
        Check(catalogPlan.Scenes[main.Name].ContentPackage == mainPackage &&
              catalogPlan.Scenes[gameOver.Name].ContentPackage is null,
            "Scene catalog supports mixed packaged and package-free Scene definitions");

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

        var transitioned = new SceneNavigator(plan.Scenes, main);
        SceneTransitionOptions fade = SceneTransitions.FadeThroughBlack(.2d, .2d);
        transitioned.SwitchTo(gameOver, fade);
        transitioned.SwitchTo(gameOver, fade);
        transitioned.BeginPendingTransition();
        Check(transitioned.Transition.Phase == SceneTransitionPhase.FadingOut &&
              transitioned.Transition.Opacity == 0f &&
              transitioned.Transition.BlocksInput &&
              !transitioned.TryTakeReady(out _),
            "A declarative fade begins at transparent old-Scene presentation and blocks input");
        transitioned.AdvanceTransition(.1d);
        Check(MathF.Abs(transitioned.Transition.Opacity - .5f) < .000001f,
            "Fade-out opacity advances in unscaled deterministic time");
        transitioned.AdvanceTransition(.1d);
        bool becameReady = transitioned.TryTakeReady(out SceneSwitchRequest ready);
        Check(transitioned.Transition.Phase == SceneTransitionPhase.Switching &&
              transitioned.Transition.Opacity == 1f &&
              becameReady && !transitioned.TryTakeReady(out _),
            "The target becomes commit-ready exactly once at full coverage");
        transitioned.Commit(gameOver);
        transitioned.CompleteSwitch(ready);
        Check(transitioned.Current == gameOver &&
              transitioned.Transition.Phase == SceneTransitionPhase.FadingIn &&
              transitioned.Transition.Opacity == 1f,
            "A committed Scene first appears fully covered before fading in");
        transitioned.AdvanceTransition(.1d);
        Check(MathF.Abs(transitioned.Transition.Opacity - .5f) < .000001f,
            "Fade-in reveals the new Scene without changing its simulation clock");
        transitioned.AdvanceTransition(.1d);
        Check(!transitioned.IsTransitioning && transitioned.Transition.Opacity == 0f,
            "A completed transition returns to an allocation-free idle snapshot");

        var failedTransition = new SceneNavigator(plan.Scenes, main);
        failedTransition.SwitchTo(gameOver, fade);
        failedTransition.BeginPendingTransition();
        failedTransition.AdvanceTransition(.2d);
        Check(failedTransition.TryTakeReady(out SceneSwitchRequest failedReady),
            "A covered transition exposes its pre-commit request");
        var expectedFailure = new IOException("expected content failure");
        failedTransition.AbortPreCommit(failedReady, expectedFailure);
        Check(failedTransition.Current == main &&
              failedTransition.LastTransitionFailure is { } failure &&
              failure.Source == main && failure.Target == gameOver &&
              ReferenceEquals(failure.Exception, expectedFailure) &&
              failedTransition.Transition.Phase == SceneTransitionPhase.FadingIn,
            "A pre-commit load failure preserves the old Scene and fades it back in");
        failedTransition.AdvanceTransition(.2d);
        Check(!failedTransition.IsTransitioning && failedTransition.Current == main,
            "Failure recovery completes without leaving a permanent input gate");

        var conflictingTransition = new SceneNavigator(plan.Scenes, main);
        conflictingTransition.SwitchTo(gameOver, fade);
        CheckThrows<InvalidOperationException>(
            () => conflictingTransition.SwitchTo(
                gameOver,
                SceneTransitions.FadeThroughBlack(.4d, .2d)),
            "The same target with conflicting transition options is rejected explicitly");
        CheckThrows<ArgumentException>(
            () => new SceneNavigator(plan.Scenes, main).SwitchTo(gameOver, default),
            "Default-initialized transition options are rejected instead of producing an invisible gate");
        CheckThrows<ArgumentOutOfRangeException>(
            () => new SceneTransitionOptions(Vector4.One, double.NaN, .2d),
            "Transition durations must be finite and non-negative");

        var allocationTransition = new SceneNavigator(plan.Scenes, main);
        allocationTransition.SwitchTo(
            gameOver,
            SceneTransitions.FadeThroughBlack(10_000d, .2d));
        allocationTransition.BeginPendingTransition();
        for (int i = 0; i < 64; i++)
        {
            allocationTransition.AdvanceTransition(1d / 60d);
            _ = allocationTransition.Transition;
        }
        long transitionAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            allocationTransition.AdvanceTransition(1d / 60d);
            _ = allocationTransition.Transition;
        }
        long transitionAllocated = GC.GetAllocatedBytesForCurrentThread() -
            transitionAllocatedBefore;
        Check(transitionAllocated == 0,
            $"Active Scene transition updates remain allocation-free ({transitionAllocated:N0} B)");
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

        ProcessMemoryDiagnostics processMemory =
            ProcessMemoryDiagnostics.CaptureCurrentProcess();
        Check(processMemory.WorkingSetBytes > 0 &&
              processMemory.PrivateBytes > 0 &&
              processMemory.VirtualBytes > 0 &&
              processMemory.ManagedHeapEstimateBytes >= 0 &&
              processMemory.GcCommittedAfterLastCollectionBytes >= 0 &&
              processMemory.GcFragmentedAfterLastCollectionBytes >= 0 &&
              !processMemory.WasFullCollectionForced,
            "Process memory capture reports positive OS counters and non-negative GC counters");
        var knownMemory = new ProcessMemoryDiagnostics(
            80, 90, 100, 200,
            20, 25, 30, 5,
            1, 2, 3, 4,
            40, 50, 60,
            false);
        Check(knownMemory.UnattributedPrivateBytes == 70,
            "Unattributed private memory excludes the GC committed heap without claiming ownership");
        var cpuAttribution = new CpuMemoryAttributionEstimate(2, 1_024, 1, 2_048);
        Check(cpuAttribution.ContributorCount == 3 &&
              cpuAttribution.TotalAttributedBytes == 3_072,
            "Managed and native ownership contributors aggregate without changing process counters");

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
                    cpuAttribution,
                    Array.Empty<CustomCpuMemoryDiagnostics>(),
                    violations,
                    processMemory);
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

    private sealed class InstanceRefLifecycleProbe : GameInstance
    {
        private int _steps;

        public InstanceRef<HostingPrefabProbe> SpawnedReference { get; private set; }
        public bool ResolvedBeforeSpawnCommit { get; private set; }
        public bool ResolvedAfterDestroyRequest { get; private set; }

        public override void OnStep(double deltaTime)
        {
            _steps++;
            if (_steps == 1)
            {
                HostingPrefabProbe spawned = Spawn(
                    new HostingPrefabProbe(new Vector2D(8f, 9f)));
                SpawnedReference = spawned.ToInstanceRef();
                ResolvedBeforeSpawnCommit = Resolve(SpawnedReference) is not null;
                return;
            }
            if (_steps != 2) return;
            Destroy(SpawnedReference);
            ResolvedAfterDestroyRequest = Resolve(SpawnedReference) is not null;
        }
    }

    private sealed class SimulationClockProbe : GameInstance
    {
        public SimulationClockSnapshot Observed { get; private set; }

        public override void OnStep(double deltaTime) => Observed = SimulationTime;
    }

    private sealed class StateHashProbe : GameInstance
    {
        private readonly GameplayRandom _random = new(0x5EEDUL);
        public GameplayHealth Health { get; } = new(10f);
        public int Score { get; set; } = 7;

        public override void OnStep(double deltaTime)
        {
            Score += _random.Range(1, 4);
        }

        protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
        {
            writer.Write("probe.score", Score);
            writer.Write("probe.health", Health);
            writer.Write("probe.random", _random.CaptureState());
        }
    }

    private readonly record struct PrimarySignal(int Value);

    private readonly record struct SecondarySignal(int Value);

    private sealed class SignalPublisher : GameInstance
    {
        public void Emit(in PrimarySignal signal) => PublishSignal(in signal);

        public void Emit(in SecondarySignal signal) => PublishSignal(in signal);
    }

    private sealed class SignalOrderProbe :
        GameInstance,
        IGameplaySignalHandler<PrimarySignal>,
        IGameplaySignalHandler<SecondarySignal>
    {
        private readonly string _name;
        private readonly List<string> _order;
        private readonly bool _republish;

        public SignalOrderProbe(
            string name,
            List<string> order,
            bool republish = false,
            bool primaryOnly = false)
        {
            _name = name;
            _order = order;
            _republish = republish;
            ListenSignal<PrimarySignal>();
            if (!primaryOnly)
                ListenSignal<SecondarySignal>();
        }

        public void OnGameplaySignal(in PrimarySignal signal)
        {
            _order.Add($"{_name}.primary.{signal.Value}");
            if (_republish && signal.Value == 1)
            {
                var next = new PrimarySignal(2);
                PublishSignal(in next);
            }
        }

        public void OnGameplaySignal(in SecondarySignal signal) =>
            _order.Add($"{_name}.secondary.{signal.Value}");

        public void ListenAfterAttach() => ListenSignal<PrimarySignal>();
    }

    private sealed class SignalSpawningProbe :
        GameInstance,
        IGameplaySignalHandler<PrimarySignal>
    {
        public SignalSpawningProbe() => ListenSignal<PrimarySignal>();

        public void OnGameplaySignal(in PrimarySignal signal) =>
            Spawn(new SignalSpawnedProbe());
    }

    private sealed class SignalSpawnedProbe : GameInstance;

    private sealed class FailingSignalProbe :
        GameInstance,
        IGameplaySignalHandler<PrimarySignal>
    {
        public FailingSignalProbe() => ListenSignal<PrimarySignal>();

        public void OnGameplaySignal(in PrimarySignal signal) =>
            throw new InvalidOperationException("Expected signal failure.");
    }

    private sealed class MissingSignalInterfaceProbe : GameInstance
    {
        public MissingSignalInterfaceProbe() => ListenSignal<PrimarySignal>();
    }

    private sealed class DuplicateSignalListenerProbe :
        GameInstance,
        IGameplaySignalHandler<PrimarySignal>
    {
        public DuplicateSignalListenerProbe()
        {
            ListenSignal<PrimarySignal>();
            ListenSignal<PrimarySignal>();
        }

        public void OnGameplaySignal(in PrimarySignal signal)
        {
        }
    }

    private sealed class CountingSignalProbe :
        GameInstance,
        IGameplaySignalHandler<PrimarySignal>
    {
        public int Count { get; private set; }

        public CountingSignalProbe() => ListenSignal<PrimarySignal>();

        public void OnGameplaySignal(in PrimarySignal signal) => Count++;
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

    private sealed class SpawnSequenceProbe : GameInstance
    {
        private readonly SpawnSequencePlayer _player;
        private readonly SpawnEmissionHandler _emit;

        public SpawnSequenceProbe(SpawnSequence sequence)
        {
            _player = new SpawnSequencePlayer(sequence);
            _emit = OnEmission;
        }

        public int EmissionCount { get; private set; }

        public override void OnStep(double deltaTime) =>
            _player.Update(deltaTime, 0, _emit);

        private void OnEmission(in SpawnEmission emission) => EmissionCount++;
    }

    private sealed class SpawnCountingSink
    {
        public int Count { get; private set; }

        public void Emit(in SpawnEmission emission) => Count++;
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

    private sealed class SceneAudioTestBackend : IAudioBackend
    {
        private readonly List<AudioBackendVoice> _playing = [];
        private long _next;

        public AudioBackendVoice Play(in AudioClipDescriptor clip, in AudioVoiceMix mix)
        {
            var voice = new AudioBackendVoice(++_next);
            _playing.Add(voice);
            return voice;
        }

        public void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix)
        {
        }

        public bool IsPlaying(AudioBackendVoice voice) => _playing.Contains(voice);

        public void Stop(AudioBackendVoice voice) => _playing.Remove(voice);

        public void CompleteOldest()
        {
            if (_playing.Count > 0) _playing.RemoveAt(0);
        }

        public void Dispose() => _playing.Clear();
    }
}
