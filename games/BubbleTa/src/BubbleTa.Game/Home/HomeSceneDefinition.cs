namespace BubbleTa.Game.Home;

using System.Numerics;
using BubbleTa.Game.Content;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Audio;
using GameEngine.Hosting;

internal static class HomeSceneDefinition {
    public static void Configure( Default2DGameContext context, bool smoke ) {
        if ( context.Content?.Id != GameAssets.Packages.BubbletaHome.Id )
            throw new InvalidOperationException(
                "BubbleTa HomeScene requires its scene-scoped Home content package." );
        if ( context.RenderViews[0].Navigation is not null )
            throw new InvalidOperationException(
                "BubbleTa HomeScene must own a fixed Camera without Viewport navigation." );
        context.Scene.Background = BackgroundConfig.Black;
        context.SceneAudio.PlayMusic( GameAssets.AudioClips.BubbletaHomeBgm );

        context.Scene.Add( new StaticHomeSpriteInstance( GameAssets.Sprites.BubbletaHomeBackground, HomeSceneLayout.BackgroundPosition,
            Vector2D.One, 10_000 ) );

        AddMeteors( context );
        AddSpots( context );
        AddStars( context );

        context.Scene.Add( new HomeBubbleInstance( GameAssets.Sprites.BubbletaHomeBubble, HomeSceneLayout.BubblePosition ) );
        context.Scene.Add( new HomeCloudInstance( GameAssets.Sprites.BubbletaHomeCloud, HomeSceneLayout.CloudPosition ) );
        context.Scene.Add(
            HomeCharacterInstance.CreateHero( GameAssets.Sprites.BubbletaHomeHeroBase, GameAssets.Sprites.BubbletaHomeHeroEffect ) );
        context.Scene.Add( HomeCharacterInstance.CreateSnow( GameAssets.Sprites.BubbletaHomeSnow ) );
        context.Scene.Add( HomeCharacterInstance.CreateKing( GameAssets.Sprites.BubbletaHomeKing ) );

        AddLogos( context );

        context.Scene.Add( new HomeWorldButtonInstance( GameAssets.Sprites.BubbletaHomeWorldEnter,
            screen => context.TryScreenToWorld( screen, out Vector2D world, out _ )
                ? world
                : null,
            () => {
                // The decoded click intentionally survives the Home -> WorldMap boundary. OpenAL
                // retains its static buffer until this one-shot Voice completes after package unload.
                context.Audio.Play( GameAssets.AudioClips.BubbletaHomeClick, AudioPlayOptions.Sfx );
                context.Scenes.SwitchTo( GameScenes.WorldMap );
            } ) );
        context.Scene.Add( new StaticHomeSpriteInstance(
            GameAssets.Sprites.BubbletaHomeSettings,
            HomeSceneLayout.SettingsPosition,
            Vector2D.One,
            -18 ) );
        context.Scene.Add( new HomeSceneController( context.Close ) );

        if ( smoke ) context.Scene.Add( new HomeSmokeProbe() );
    }

    private static void AddMeteors( Default2DGameContext context ) {
        ReadOnlySpan<Vector2D> placements = HomeSceneLayout.Meteors;
        for (int i = 0; i < placements.Length; i++) {
            context.Scene.Add( new HomeMeteorInstance(
                GameAssets.Sprites.BubbletaHomeMeteor,
                placements[i],
                HomeSceneLayout.SeedFor( i ) ) );
        }
    }

    private static void AddSpots( Default2DGameContext context ) {
        ReadOnlySpan<Vector2D> placements = HomeSceneLayout.Spots;
        for (int i = 0; i < placements.Length; i++) {
            context.Scene.Add( new HomeSpotInstance(
                GameAssets.Sprites.BubbletaHomeSpot,
                placements[i],
                HomeSceneLayout.SeedFor( 100 + i ) ) );
        }
    }

    private static void AddStars( Default2DGameContext context ) {
        foreach ( SpritePlacement placement in HomeSceneLayout.Stars ) {
            context.Scene.Add( new HomeStarInstance(
                GameAssets.Sprites.BubbletaHomeStar,
                placement ) );
        }
    }

    private static void AddLogos( Default2DGameContext context ) {
        var sprites = new[] {
            GameAssets.Sprites.BubbletaHomeLogo01, GameAssets.Sprites.BubbletaHomeLogo02, GameAssets.Sprites.BubbletaHomeLogo03,
            GameAssets.Sprites.BubbletaHomeLogo04, GameAssets.Sprites.BubbletaHomeLogo05, GameAssets.Sprites.BubbletaHomeLogo06,
            GameAssets.Sprites.BubbletaHomeLogo07, GameAssets.Sprites.BubbletaHomeLogo08, GameAssets.Sprites.BubbletaHomeLogo09,
            GameAssets.Sprites.BubbletaHomeLogo10, GameAssets.Sprites.BubbletaHomeLogo11, GameAssets.Sprites.BubbletaHomeLogo12
        };
        ReadOnlySpan<LogoPlacement> placements = HomeSceneLayout.Logos;
        for (int i = 0; i < placements.Length; i++)
            context.Scene.Add( new LogoRevealInstance( sprites[i], placements[i] ) );
    }
}

internal static class WorldMapPlaceholderScene {
    public static void Configure( Default2DGameContext context, bool smoke ) {
        if ( context.Content is not null )
            throw new InvalidOperationException(
                "The package-free WorldMap placeholder must not retain Home content." );
        if ( context.RenderViews[0].Navigation is null )
            throw new InvalidOperationException(
                "BubbleTa WorldMapScene requires its Scene-owned Viewport navigation." );
        context.Scene.Background = BackgroundConfig.FromColor(
            new Vector4( .035f, .12f, .24f, 1f ) );
        context.Scene.Add( new WorldMapPlaceholderController( () => context.Scenes.SwitchTo( GameScenes.Home ) ) );
        if ( smoke ) context.Scene.Add( new WorldMapSmokeProbe( context.Close ) );
    }
}
