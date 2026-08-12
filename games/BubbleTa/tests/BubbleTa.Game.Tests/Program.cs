namespace BubbleTa.Game.Tests;

using System.Numerics;
using BubbleTa.Game.Content;
using BubbleTa.Game.Home;
using BubbleTa.Game.WorldMap;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

internal static class Program
{
    private static int _passed;

    private static int Main()
    {
        Run("Legacy layout is centralized", LegacyLayoutIsCentralized);
        Run("Home audio references are generated", HomeAudioReferencesAreGenerated);
        Run("WorldMap content references are generated", WorldMapContentReferencesAreGenerated);
        Run("WorldMap first island layout", WorldMapFirstIslandLayout);
        Run("WorldMap node presentation", WorldMapNodePresentation);
        Run("WorldMap cloud motion", WorldMapCloudMotion);
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
        WorldMapNodePlacement availablePlacement = WorldMapSceneLayout.FirstIslandNodes[0];
        var available = new WorldMapLevelNodeInstance(
            WorldMapSceneDefinition.SelectNodeSprite(availablePlacement.Kind, false),
            availablePlacement,
            false);
        Check(available.Level == 1 && !available.IsLocked &&
              available.Sprite == GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            "Level one must be the only available read-only node.");

        WorldMapNodePlacement lockedPlacement = WorldMapSceneLayout.FirstIslandNodes[3];
        var locked = new WorldMapLevelNodeInstance(
            WorldMapSceneDefinition.SelectNodeSprite(lockedPlacement.Kind, true),
            lockedPlacement,
            true);
        Check(locked.Level == 4 && locked.Kind == WorldMapNodeKind.Timed &&
              locked.IsLocked && locked.Sprite == GameAssets.Sprites.BubbletaWorldMapLevelSpotLock,
            "Locked nodes must retain authored kinds while presenting the lock Sprite.");
        Check(WorldMapSceneDefinition.SelectNodeSprite(WorldMapNodeKind.Timed, false) ==
              GameAssets.Sprites.BubbletaWorldMapLevelSpotTime &&
              WorldMapSceneDefinition.SelectNodeSprite(WorldMapNodeKind.Moving, false) ==
              GameAssets.Sprites.BubbletaWorldMapLevelSpotMove,
            "Unlocked special node kinds must map to their dedicated Sprites.");
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
