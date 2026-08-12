namespace BubbleTa.Game.Tests;

using System.Numerics;
using BubbleTa.Game.Content;
using BubbleTa.Game.Home;
using BubbleTa.Game.WorldMap;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Camera.Domain;
using GameEngine.Features.ViewportNavigation;

internal static class Program
{
    private static int _passed;

    private static int Main()
    {
        Run("Legacy layout is centralized", LegacyLayoutIsCentralized);
        Run("Home audio references are generated", HomeAudioReferencesAreGenerated);
        Run("WorldMap content references are generated", WorldMapContentReferencesAreGenerated);
        Run("WorldMap first island layout", WorldMapFirstIslandLayout);
        Run("WorldMap horizontal rubber band", WorldMapHorizontalRubberBand);
        Run("WorldMap progress snapshot", WorldMapProgressSnapshotStates);
        Run("WorldMap node presentation", WorldMapNodePresentation);
        Run("WorldMap node selection", WorldMapNodeSelection);
        Run("WorldMap cloud motion", WorldMapCloudMotion);
        Run("WorldMap decoration layout", WorldMapDecorationLayout);
        Run("WorldMap decoration timing", WorldMapDecorationTiming);
        Run("WorldMap decoration determinism", WorldMapDecorationDeterminism);
        Run("Logo reveal timing", LogoRevealTiming);
        Run("Periodic decorations", PeriodicDecorations);
        Run("Character entrance and idle", CharacterEntranceAndIdle);
        Run("Meteor determinism", MeteorDeterminism);
        Run("Spot determinism", SpotDeterminism);
        Run("World button capture", WorldButtonCapture);
        Run("Escape callbacks", EscapeCallbacks);
        Console.WriteLine($"BubbleTa.Game.Tests passed: {_passed}");
        return 0;
    }

    private static void HomeAudioReferencesAreGenerated()
    {
        Check(GameAssets.AudioClips.BubbletaHomeBgm.Name == "bubbleta.home.bgm",
            "Home BGM must have a strongly typed generated reference.");
        Check(GameAssets.AudioClips.BubbletaHomeClick.Name == "bubbleta.home.click",
            "Home click SFX must have a strongly typed generated reference.");
    }

    private static void WorldMapContentReferencesAreGenerated()
    {
        Check(GameAssets.Packages.BubbletaWorldMap.Id == "bubbleta.world-map",
            "WorldMap package must have a strongly typed generated reference.");
        Check(GameAssets.AudioClips.BubbletaWorldMapBgm.Name == "bubbleta.world-map.bgm",
            "WorldMap BGM must have a strongly typed generated reference.");
        Check(GameAssets.Sprites.BubbletaWorldMapIslandUpper.Name ==
              "bubbleta.world-map.island-upper" &&
              GameAssets.Sprites.BubbletaWorldMapIslandLower.Name ==
              "bubbleta.world-map.island-lower",
            "Both WorldMap island halves must have generated Sprite references.");
        Check(GameAssets.Sprites.BubbletaWorldMapDecorationSmoke.Name ==
              "bubbleta.world-map.decoration.smoke" &&
              GameAssets.Sprites.BubbletaWorldMapDecorationBird.Name ==
              "bubbleta.world-map.decoration.bird" &&
              GameAssets.Sprites.BubbletaWorldMapDecorationLuteaFish.Name ==
              "bubbleta.world-map.decoration.lutea-fish",
            "WorldMap scenery must have strongly typed generated Sprite references.");
    }

    private static void WorldMapFirstIslandLayout()
    {
        ReadOnlySpan<WorldMapNodePlacement> nodes = WorldMapSceneLayout.FirstIslandNodes;
        Check(nodes.Length == 20, "First island must retain twenty authored level nodes.");
        for (int i = 0; i < nodes.Length; i++)
            Check(nodes[i].Level == i + 1, "First island level IDs must remain ordered 1 through 20.");

        Check(WorldMapSceneLayout.IslandUpperPosition == new Vector2D(538f, 13_300f) &&
              WorldMapSceneLayout.IslandLowerPosition == new Vector2D(538f, 15_972f),
            "First island halves must retain the legacy anchor positions.");
        float upperBottom = WorldMapSceneLayout.IslandUpperPosition.Y + 668f * 2f;
        float lowerTop = WorldMapSceneLayout.IslandLowerPosition.Y - 668f * 2f;
        Near(upperBottom, WorldMapSceneLayout.FirstIslandSeamY, .001f,
            "Upper island half must end at the authored seam.");
        Near(lowerTop, WorldMapSceneLayout.FirstIslandSeamY, .001f,
            "Lower island half must begin at the authored seam.");

        Check(nodes[0].Position == new Vector2D(511f, 15_545f) &&
              nodes[^1].Position == new Vector2D(460f, 13_628f),
            "First and twentieth nodes must retain the legacy endpoints.");
        int timed = 0;
        int moving = 0;
        foreach (WorldMapNodePlacement node in nodes)
        {
            if (node.Kind == WorldMapNodeKind.Timed) timed++;
            if (node.Kind == WorldMapNodeKind.Moving) moving++;
        }
        Check(timed == 4 && moving == 3,
            "First island must retain four Timed and three Moving node types.");
    }

    private static void WorldMapNodePresentation()
    {
        var progress = new WorldMapProgressSnapshot(3, [3, 2]);
        WorldMapNodePlacement completedPlacement = WorldMapSceneLayout.FirstIslandNodes[0];
        WorldMapLevelNodeInstance completed = CreateNode(completedPlacement, progress);
        Check(completed.Level == 1 && completed.State == WorldMapLevelState.Completed &&
              completed.Stars == 3 && !completed.IsLocked && completed.Color.X < 1f &&
              completed.Sprite == GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            "Completed nodes must retain their authored kind, stars, and subdued presentation.");

        WorldMapNodePlacement availablePlacement = WorldMapSceneLayout.FirstIslandNodes[2];
        WorldMapLevelNodeInstance available = CreateNode(availablePlacement, progress);
        Check(available.Level == 3 && available.State == WorldMapLevelState.Available &&
              available.Sprite == GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            "The highest unlocked level must use its authored available Sprite.");

        WorldMapNodePlacement lockedPlacement = WorldMapSceneLayout.FirstIslandNodes[3];
        WorldMapLevelNodeInstance locked = CreateNode(lockedPlacement, progress);
        Check(locked.Level == 4 && locked.Kind == WorldMapNodeKind.Timed &&
              locked.IsLocked && locked.Sprite == GameAssets.Sprites.BubbletaWorldMapLevelSpotLock,
            "Locked nodes must retain authored kinds while presenting the lock Sprite.");
        Check(WorldMapSceneDefinition.SelectNodeSprite(
                  WorldMapNodeKind.Timed, WorldMapLevelState.Available) ==
              GameAssets.Sprites.BubbletaWorldMapLevelSpotTime &&
              WorldMapSceneDefinition.SelectNodeSprite(
                  WorldMapNodeKind.Moving, WorldMapLevelState.Completed) ==
              GameAssets.Sprites.BubbletaWorldMapLevelSpotMove,
            "Unlocked special node kinds must map to their dedicated Sprites.");
    }

    private static void WorldMapHorizontalRubberBand()
    {
        var camera = new Camera2D(new Vector2(
            WorldMapSceneLayout.ViewWidth,
            1_280f))
        {
            Position = WorldMapSceneLayout.InitialCameraPosition
        };
        ViewportController viewport = new ViewportNavigationBuilder()
            .Bounce(WorldMapSceneLayout.NavigationBounce)
            .Build()
            .CreateController(camera);

        viewport.MoveByWorld(new Vector2(100f, -1_000f));
        viewport.Update(ViewportInputFrame.Empty, .15d);

        Near(viewport.VisibleWorldBounds.Left, 164f, .001f,
            "Horizontal overscroll must return to the authored View left edge.");
        Near(viewport.VisibleWorldBounds.Top, 13_820f, .001f,
            "A vertically valid position must remain unchanged during horizontal Bounce.");
    }

    private static void WorldMapProgressSnapshotStates()
    {
        byte[] stars = [3, 1, 0];
        var progress = new WorldMapProgressSnapshot(4, stars);
        stars[0] = 0;

        Check(progress.HighestUnlockedLevel == 4 &&
              progress.GetState(1) == WorldMapLevelState.Completed &&
              progress.GetState(4) == WorldMapLevelState.Available &&
              progress.GetState(5) == WorldMapLevelState.Locked,
            "A progress snapshot must derive completed, available, and locked states.");
        Check(progress.GetStars(1) == 3 && progress.GetStars(2) == 1 &&
              progress.GetStars(4) == 0,
            "A progress snapshot must defensively retain optional completion stars.");
        Throws<ArgumentOutOfRangeException>(
            () => _ = new WorldMapProgressSnapshot(0, []),
            "Progress must reject an invalid highest unlocked level.");
        Throws<ArgumentOutOfRangeException>(
            () => _ = new WorldMapProgressSnapshot(2, [4]),
            "Progress must reject more than three stars.");
        Throws<ArgumentException>(
            () => _ = new WorldMapProgressSnapshot(2, [0, 1]),
            "The currently available level cannot already contain completion stars.");
    }

    private static void WorldMapNodeSelection()
    {
        WorldMapNodePlacement placement = WorldMapSceneLayout.FirstIslandNodes[0];
        Vector2D inside = placement.Position;
        Vector2D outside = placement.Position + new Vector2D(100f, 100f);
        var controller = new WorldMapController(() => { });
        var selectable = new WorldMapLevelNodeInstance(
            GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            placement,
            WorldMapLevelState.Available,
            0,
            static screen => screen,
            controller.RequestSelection);

        selectable.UpdatePointer(inside, inside, true);
        Check(selectable.IsCaptured, "An unlocked node must capture an inside press.");
        selectable.UpdatePointer(inside, inside, false);
        Check(selectable.WasSelected && controller.LastSelection is { Level: 1 },
            "An inside press-release must emit one typed selection request.");

        int draggedSelections = 0;
        var dragged = new WorldMapLevelNodeInstance(
            selectable.Sprite, placement, WorldMapLevelState.Available, 0,
            static screen => screen, _ => draggedSelections++);
        dragged.UpdatePointer(inside, inside, true);
        dragged.UpdatePointer(inside, inside + new Vector2D(9f, 0f), true);
        dragged.UpdatePointer(inside, inside + new Vector2D(9f, 0f), false);
        Check(draggedSelections == 0 && !dragged.IsCaptured,
            "Moving beyond the Viewport drag threshold must cancel node selection.");

        int lockedSelections = 0;
        var locked = new WorldMapLevelNodeInstance(
            GameAssets.Sprites.BubbletaWorldMapLevelSpotLock,
            placement,
            WorldMapLevelState.Locked,
            0,
            static screen => screen,
            _ => lockedSelections++);
        locked.UpdatePointer(inside, inside, true);
        locked.UpdatePointer(inside, inside, false);
        locked.UpdatePointer(outside, outside, true);
        locked.UpdatePointer(inside, inside, false);
        Check(lockedSelections == 0 && !locked.WasSelected,
            "Locked nodes and outside presses must never request a selection.");
    }

    private static void WorldMapCloudMotion()
    {
        WorldMapCloudPlacement placement = WorldMapSceneLayout.UnderClouds[0];
        var first = new WorldMapCloudInstance(
            GameAssets.Sprites.BubbletaWorldMapCloudUnder,
            placement);
        var second = new WorldMapCloudInstance(
            GameAssets.Sprites.BubbletaWorldMapCloudUnder,
            placement);
        first.OnStep(3.5d);
        second.OnStep(3.5d);
        Check(first.Position == second.Position,
            "Authored cloud motion must be deterministic for equal placements and elapsed time.");
        Check(first.Position != placement.Position,
            "Clouds must drift around their authored origin.");
        Check(WorldMapSceneLayout.UnderClouds.Length == 8 &&
              WorldMapSceneLayout.AboveClouds.Length == 6,
            "First island must have deterministic rear and foreground cloud layers.");
    }

    private static void WorldMapDecorationLayout()
    {
        Check(WorldMapSceneLayout.People.Length == 3 &&
              WorldMapSceneLayout.Apples.Length == 3,
            "First island must retain three people and three apple effects.");
        Check(WorldMapSceneLayout.SmokePosition == new Vector2D(220f, 15_360f) &&
              WorldMapSceneLayout.StonePosition == new Vector2D(558f, 14_936f) &&
              WorldMapSceneLayout.MushroomPosition == new Vector2D(560f, 15_625f) &&
              WorldMapSceneLayout.LuteaPosition == new Vector2D(466f, 14_485f),
            "First-island scenery must retain the legacy world coordinates.");
        Near(WorldMapSceneLayout.People[0].RotationRadians, MathF.PI / 6f, .001f,
            "The first jumping person must retain its authored rotation.");
        Near(WorldMapSceneLayout.People[1].RotationRadians, 0f, .001f,
            "The vertical jumping person must remain upright.");

        var bird = new WorldMapBirdInstance(
            new SpriteRef("test.bird.depth"), WorldMapSceneLayout.BirdPosition, 1UL);
        WorldMapLevelNodeInstance node = CreateNode(
            WorldMapSceneLayout.FirstIslandNodes[0], WorldMapProgressSnapshot.NewGame);
        Check(bird.Depth < node.Depth,
            "Birds must render above level nodes; smaller Depth values are drawn later.");
    }

    private static void WorldMapDecorationTiming()
    {
        var smoke = new WorldMapSmokeInstance(
            new SpriteRef("test.smoke"), WorldMapSceneLayout.SmokePosition);
        smoke.OnStep(2.99d);
        Check(smoke.Scale == Vector2D.Zero,
            "Smoke must remain hidden before its initial delay.");
        smoke.OnStep(.51d);
        Check(smoke.Scale.X > 0f && smoke.Position.Y < WorldMapSceneLayout.SmokePosition.Y &&
              smoke.Color.W < 1f,
            "Active smoke must grow, rise, and fade together.");

        WorldMapPersonPlacement personPlacement = WorldMapSceneLayout.People[0];
        var person = new WorldMapPersonInstance(new SpriteRef("test.person"), personPlacement);
        person.OnStep(personPlacement.InitialDelaySeconds + .5d);
        Check(person.Position == personPlacement.Position + personPlacement.JumpOffset,
            "A person must reach the authored jump offset after its entrance tween.");
        person.OnStep(2d);
        Check(person.Position == personPlacement.Position,
            "A person must return to its authored resting point.");

        var lutea = new WorldMapLuteaInstance(
            new SpriteRef("test.lutea"),
            new SpriteRef("test.lutea.effect"),
            WorldMapSceneLayout.LuteaPosition,
            42UL);
        lutea.OnStep(25d / 13.8d + .01d);
        Check(!lutea.IsPlaying && lutea.ImageIndex == 24f,
            "The water-side effect must pause on its final frame between loops.");
    }

    private static void WorldMapDecorationDeterminism()
    {
        var firstBird = new WorldMapBirdInstance(
            new SpriteRef("test.bird"), WorldMapSceneLayout.BirdPosition, 77UL);
        var secondBird = new WorldMapBirdInstance(
            new SpriteRef("test.bird"), WorldMapSceneLayout.BirdPosition, 77UL);
        StepBoth(firstBird.OnStep, secondBird.OnStep, 12d);
        Check(firstBird.Position == secondBird.Position &&
              firstBird.Scale == secondBird.Scale &&
              firstBird.Color == secondBird.Color,
            "Equal bird seeds and elapsed time must produce equal flights.");
        Check(firstBird.Position.Y >= WorldMapSceneLayout.BirdPosition.Y - 400f &&
              firstBird.Position.Y <= WorldMapSceneLayout.BirdPosition.Y + 400f,
            "Bird flights must stay inside the authored random Y band.");

        WorldMapApplePlacement placement = WorldMapSceneLayout.Apples[0];
        var firstApple = new WorldMapAppleInstance(new SpriteRef("test.apple"), placement);
        var secondApple = new WorldMapAppleInstance(new SpriteRef("test.apple"), placement);
        StepBoth(firstApple.OnStep, secondApple.OnStep, 12d);
        Check(firstApple.Phase == secondApple.Phase &&
              firstApple.Position == secondApple.Position &&
              firstApple.Scale == secondApple.Scale &&
              firstApple.Color == secondApple.Color,
            "Equal apple seeds and elapsed time must produce equal effect states.");
    }

    private static void LegacyLayoutIsCentralized()
    {
        Check(HomeSceneLayout.Logos.Length == 12, "Expected twelve Logo placements.");
        Check(HomeSceneLayout.Stars.Length == 3, "Expected three star placements.");
        Check(HomeSceneLayout.Spots.Length == 5, "Expected five spot placements.");
        Check(HomeSceneLayout.Meteors.Length == 5, "Expected five meteor placements.");
        Check(HomeSceneLayout.CameraPosition == new Vector2D(120f, 0f),
            "Legacy central crop must start at x=120.");
        Check(HomeSceneLayout.WorldButtonPosition == new Vector2D(512f, 1056f),
            "World button must retain its legacy Room position.");
        Check(WorldMapSceneLayout.RoomBounds.Width == 1_048f &&
              WorldMapSceneLayout.RoomBounds.Height == 16_100f &&
              WorldMapSceneLayout.InitialCameraPosition == new Vector2(164f, 14_820f),
            "WorldMap Scene View must retain the legacy Room and bottom Camera boundary.");
        Check(WorldMapSceneLayout.NavigationDrag.Axis ==
              GameEngine.Features.ViewportNavigation.ViewportAxis.All &&
              WorldMapSceneLayout.NavigationDrag.AxisLock ==
              GameEngine.Features.ViewportNavigation.ViewportDragAxisLock.Dominant &&
              WorldMapSceneLayout.NavigationBounce.Axis ==
              GameEngine.Features.ViewportNavigation.ViewportAxis.All,
            "WorldMap navigation must lock each Drag gesture to one axis, then bounce on both axes.");
        Check(WorldMapSceneLayout.NavigationBounds.Left == 164f &&
              WorldMapSceneLayout.NavigationBounds.Right == 884f &&
              WorldMapSceneLayout.NavigationBounds.Top == 0f &&
              WorldMapSceneLayout.NavigationBounds.Bottom == 16_100f,
            "WorldMap navigation must keep vertical world travel while horizontally bouncing to the authored View.");
        Check(WorldMapSceneLayout.NavigationBounce.WorldBounds ==
              WorldMapSceneLayout.NavigationBounds,
            "WorldMap Bounce must use the narrow navigation boundary instead of the full Room width.");
    }

    private static void LogoRevealTiming()
    {
        LogoPlacement placement = HomeSceneLayout.Logos[0];
        var logo = new LogoRevealInstance(new SpriteRef("test.logo"), placement);
        logo.OnStep(placement.DelaySeconds - .001d);
        Check(!logo.IsRevealed && logo.Scale == Vector2D.Zero,
            "Logo must stay hidden before its delay.");
        logo.OnStep(.201d);
        Check(logo.IsRevealed, "Logo must reveal after its delay.");
        Near(logo.Scale.X, 1f, .001f, "Logo scale must settle at one.");

        LogoPlacement alphaPlacement = HomeSceneLayout.Logos[6];
        var alpha = new LogoRevealInstance(new SpriteRef("test.alpha"), alphaPlacement);
        alpha.OnStep(alphaPlacement.DelaySeconds + .2d);
        Near(alpha.Color.W, 1f, .001f, "Alpha Logo must settle opaque.");

        LogoPlacement dropPlacement = HomeSceneLayout.Logos[7];
        var drop = new LogoRevealInstance(new SpriteRef("test.drop"), dropPlacement);
        drop.OnStep(dropPlacement.DelaySeconds + .4d);
        Near(drop.Position.Y, dropPlacement.Position.Y, .001f,
            "Drop Logo must settle at its authored Y.");
        Near(drop.Scale.X, 1f, .001f, "Drop Logo must settle at scale one.");
    }

    private static void PeriodicDecorations()
    {
        SpritePlacement starPlacement = HomeSceneLayout.Stars[0];
        var star = new HomeStarInstance(new SpriteRef("test.star"), starPlacement);
        star.OnStep(.45d);
        Near(star.Scale.X, 1f, .001f, "Star must reach base scale after one way.");
        star.OnStep(.45d);
        Near(star.Scale.X, 1.2f, .001f, "Star must return to enlarged scale.");

        var bubble = new HomeBubbleInstance(
            new SpriteRef("test.bubble"), HomeSceneLayout.BubblePosition);
        bubble.OnStep(.79d);
        Check(bubble.Scale == Vector2D.Zero, "Bubble must wait for its reveal delay.");
        bubble.OnStep(.16d);
        Check(bubble.Scale.X >= 1f, "Bubble must enter its pulse after reveal.");

        var synchronizedBubble = new HomeBubbleInstance(
            new SpriteRef("test.bubble.synchronized"), HomeSceneLayout.BubblePosition);
        var synchronizedSnow = HomeCharacterInstance.CreateSnow(
            new SpriteRef("test.snow.synchronized"));
        synchronizedBubble.OnStep(.95d);
        synchronizedSnow.OnStep(.95d);
        AssertBubbleAndSnowPhase(synchronizedBubble, synchronizedSnow,
            "Bubble and Snow must share their idle phase after reveal.");
        synchronizedBubble.OnStep(1d);
        synchronizedSnow.OnStep(1d);
        AssertBubbleAndSnowPhase(synchronizedBubble, synchronizedSnow,
            "Bubble and Snow must remain synchronized at the far endpoint.");

        var cloud = new HomeCloudInstance(
            new SpriteRef("test.cloud"), HomeSceneLayout.CloudPosition);
        cloud.OnStep(2d);
        Near(cloud.Position.X, HomeSceneLayout.CloudPosition.X, .001f,
            "Cloud must complete its horizontal entrance.");
        cloud.OnStep(1.2d);
        Near(cloud.Position.Y, HomeSceneLayout.CloudPosition.Y + 20f, .001f,
            "Cloud must reach the bottom of its bob.");
    }

    private static void AssertBubbleAndSnowPhase(
        HomeBubbleInstance bubble,
        HomeCharacterInstance snow,
        string message)
    {
        float bubblePhase = (1.02f - bubble.Scale.X) / .02f;
        float snowPhase = (snow.Position.Y - HomeSceneLayout.SnowPosition.Y) / 20f;
        Near(bubblePhase, snowPhase, .001f, message);
    }

    private static void CharacterEntranceAndIdle()
    {
        var snow = HomeCharacterInstance.CreateSnow(new SpriteRef("test.snow"));
        snow.OnStep(.19d);
        Check(snow.Scale == Vector2D.Zero, "Snow must stay hidden before its delay.");
        snow.OnStep(.16d);
        Near(snow.Position.X, HomeSceneLayout.SnowPosition.X, .001f,
            "Snow must finish its entrance.");
        Near(snow.Scale.X, 1f, .001f, "Snow must finish at scale one.");
        snow.OnStep(.4d);
        Near(snow.Position.Y, HomeSceneLayout.SnowPosition.Y, .001f,
            "Snow idle begins at its authored position.");
        snow.OnStep(1.2d);
        Near(snow.Position.Y, HomeSceneLayout.SnowPosition.Y + 20f, .001f,
            "Snow must reach its positive bob offset.");

        var hero = HomeCharacterInstance.CreateHero(
            new SpriteRef("test.hero.base"),
            new SpriteRef("test.hero.effect"));
        hero.OnStep(.59d);
        Near(hero.Color.W, 0f, .001f, "Hero must remain hidden before its delay.");
        hero.OnStep(.16d);
        Near(hero.Position.Y, HomeSceneLayout.HeroPosition.Y, .001f,
            "Hero must finish its entrance.");
        Near(hero.ImageSpeed, 1f, .001f,
            "Hero effect animation must start with idle motion.");
    }

    private static void MeteorDeterminism()
    {
        var first = new HomeMeteorInstance(
            new SpriteRef("test.meteor"),
            HomeSceneLayout.Meteors[0],
            HomeSceneLayout.SeedFor(0));
        var second = new HomeMeteorInstance(
            new SpriteRef("test.meteor"),
            HomeSceneLayout.Meteors[0],
            HomeSceneLayout.SeedFor(0));
        Check(first.NextResetSeconds >= 1d && first.NextResetSeconds < 6d,
            "Meteor initial delay must be in [1, 6).");
        Near(first.NextResetSeconds, second.NextResetSeconds, .000001d,
            "Equal seeds must produce equal meteor delays.");
        StepBoth(first.OnStep, second.OnStep, 12d);
        Near(first.Position.X, second.Position.X, .001f,
            "Equal seeds must produce equal meteor X positions.");
        Near(first.Position.Y, second.Position.Y, .001f,
            "Equal seeds must produce equal meteor Y positions.");
        Check(first.IsMoving && HomeMeteorInstance.Velocity.X < 0f &&
              HomeMeteorInstance.Velocity.Y > 0f,
            "Meteor must move toward the lower-left.");
    }

    private static void SpotDeterminism()
    {
        var first = new HomeSpotInstance(
            new SpriteRef("test.spot"),
            HomeSceneLayout.Spots[0],
            HomeSceneLayout.SeedFor(100));
        var second = new HomeSpotInstance(
            new SpriteRef("test.spot"),
            HomeSceneLayout.Spots[0],
            HomeSceneLayout.SeedFor(100));
        Check(first.NextFadeSeconds >= 3d && first.NextFadeSeconds < 6d,
            "Spot delay must be in [3, 6).");
        Near(first.NextFadeSeconds, second.NextFadeSeconds, .000001d,
            "Equal seeds must produce equal spot delays.");
        StepBoth(first.OnStep, second.OnStep, 8d);
        Near(first.Color.W, second.Color.W, .000001f,
            "Equal seeds must produce equal spot alpha.");
        Check(first.IsFading == second.IsFading,
            "Equal seeds must produce equal spot phases.");
    }

    private static void WorldButtonCapture()
    {
        int activations = 0;
        var button = new HomeWorldButtonInstance(
            new SpriteRef("test.world"),
            _ => null,
            () => activations++);
        button.OnStep(1.21d);
        Check(button.IsRevealed, "World button must become interactive after reveal.");

        Vector2D inside = HomeSceneLayout.WorldButtonPosition;
        Vector2D outside = new(0f, 0f);
        button.UpdatePointer(inside, true);
        button.UpdatePointer(inside, false);
        Check(activations == 1 && button.WasActivated,
            "Inside press-release must activate exactly once.");
        button.UpdatePointer(inside, true);
        button.UpdatePointer(inside, false);
        Check(activations == 1, "Activated button must not request a second switch.");

        int draggedActivations = 0;
        var dragged = CreateRevealedButton(() => draggedActivations++);
        dragged.UpdatePointer(inside, true);
        dragged.UpdatePointer(outside, false);
        Check(draggedActivations == 0, "Release outside must cancel capture.");

        int outsideActivations = 0;
        var outsidePress = CreateRevealedButton(() => outsideActivations++);
        outsidePress.UpdatePointer(outside, true);
        outsidePress.UpdatePointer(inside, false);
        Check(outsideActivations == 0, "Press outside must not capture the button.");
    }

    private static void EscapeCallbacks()
    {
        int closed = 0;
        var home = new HomeSceneController(() => closed++);
        home.OnKeyDown(InputKey.Enter);
        home.OnKeyDown(InputKey.Escape);
        Check(closed == 1, "Home Escape must close once.");

        int returned = 0;
        var world = new WorldMapController(() => returned++);
        world.OnKeyDown(InputKey.Space);
        world.OnKeyDown(InputKey.Escape);
        Check(returned == 1, "WorldMap Escape must return Home once.");
    }

    private static HomeWorldButtonInstance CreateRevealedButton(Action activate)
    {
        var button = new HomeWorldButtonInstance(
            new SpriteRef("test.world"),
            _ => null,
            activate);
        button.OnStep(1.21d);
        return button;
    }

    private static WorldMapLevelNodeInstance CreateNode(
        in WorldMapNodePlacement placement,
        WorldMapProgressSnapshot progress)
    {
        WorldMapLevelState state = progress.GetState(placement.Level);
        return new WorldMapLevelNodeInstance(
            WorldMapSceneDefinition.SelectNodeSprite(placement.Kind, state),
            placement,
            state,
            progress.GetStars(placement.Level),
            static screen => screen,
            static _ => { });
    }

    private static void StepBoth(
        Action<double> first,
        Action<double> second,
        double seconds)
    {
        const double delta = 1d / 60d;
        int steps = (int)Math.Round(seconds / delta);
        for (int i = 0; i < steps; i++)
        {
            first(delta);
            second(delta);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {name}: {exception}");
            Environment.Exit(1);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action, string message)
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

        throw new InvalidOperationException(message);
    }

    private static void Near(float actual, float expected, float tolerance, string message)
    {
        if (MathF.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
    }

    private static void Near(double actual, double expected, double tolerance, string message)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
    }
}
