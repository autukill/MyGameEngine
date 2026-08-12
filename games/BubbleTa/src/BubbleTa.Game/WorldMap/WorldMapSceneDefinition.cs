namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using BubbleTa.Game.Content;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Hosting;

internal static class WorldMapSceneDefinition {
    public static void Configure( Default2DGameContext context, bool smoke ) {
        if ( context.Content?.Id != GameAssets.Packages.BubbletaWorldMap.Id )
            throw new InvalidOperationException(
                "BubbleTa WorldMapScene requires its scene-scoped WorldMap content package." );
        if ( context.RenderViews[0].Navigation is null )
            throw new InvalidOperationException(
                "BubbleTa WorldMapScene requires its Scene-owned Viewport navigation." );

        context.Scene.Background = BackgroundConfig.FromColor(
            new Vector4( 108f / 255f, 128f / 255f, 223f / 255f, 1f ) );
        context.SceneAudio.PlayMusic( GameAssets.AudioClips.BubbletaWorldMapBgm );

        AddClouds( context, GameAssets.Sprites.BubbletaWorldMapCloudUnder,
            WorldMapSceneLayout.UnderClouds );

        context.Scene.Add( new WorldMapIslandInstance(
            GameAssets.Sprites.BubbletaWorldMapIslandUpper,
            WorldMapSceneLayout.IslandUpperPosition ) );
        context.Scene.Add( new WorldMapIslandInstance(
            GameAssets.Sprites.BubbletaWorldMapIslandLower,
            WorldMapSceneLayout.IslandLowerPosition ) );

        AddDecorations( context );
        AddLevelNodes( context );
        AddClouds( context, GameAssets.Sprites.BubbletaWorldMapCloudAbove,
            WorldMapSceneLayout.AboveClouds );

        context.Scene.Add( new WorldMapController(
            () => context.Scenes.SwitchTo( GameScenes.Home ) ) );
        if ( smoke ) context.Scene.Add( new WorldMapSmokeProbe( context.Close ) );
    }

    private static void AddDecorations( Default2DGameContext context ) {
        context.Scene.Add( new WorldMapSmokeInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationSmoke,
            WorldMapSceneLayout.SmokePosition ) );
        context.Scene.Add( new WorldMapStaticDecorationInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationStone,
            WorldMapSceneLayout.StonePosition,
            19 ) );
        context.Scene.Add( new WorldMapMushroomInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationMushroom,
            WorldMapSceneLayout.MushroomPosition ) );
        context.Scene.Add( new WorldMapBirdInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationBird,
            WorldMapSceneLayout.BirdPosition,
            0xB17D_0001UL ) );
        context.Scene.Add( new WorldMapLuteaInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationLutea,
            GameAssets.Sprites.BubbletaWorldMapDecorationLuteaFish,
            WorldMapSceneLayout.LuteaPosition,
            0xF157_0001UL ) );

        SpriteRef[] people = [
            GameAssets.Sprites.BubbletaWorldMapDecorationPerson0,
            GameAssets.Sprites.BubbletaWorldMapDecorationPerson1,
            GameAssets.Sprites.BubbletaWorldMapDecorationPerson2
        ];
        ReadOnlySpan<WorldMapPersonPlacement> placements = WorldMapSceneLayout.People;
        for (int i = 0; i < placements.Length; i++)
            context.Scene.Add( new WorldMapPersonInstance( people[i], placements[i] ) );

        foreach ( WorldMapApplePlacement placement in WorldMapSceneLayout.Apples )
            context.Scene.Add( new WorldMapAppleInstance(
                GameAssets.Sprites.BubbletaWorldMapDecorationApple,
                placement ) );
    }

    private static void AddClouds(
        Default2DGameContext context,
        SpriteRef sprite,
        ReadOnlySpan<WorldMapCloudPlacement> placements ) {
        foreach ( WorldMapCloudPlacement placement in placements )
            context.Scene.Add( new WorldMapCloudInstance( sprite, placement ) );
    }

    private static void AddLevelNodes( Default2DGameContext context ) {
        ReadOnlySpan<WorldMapNodePlacement> nodes = WorldMapSceneLayout.FirstIslandNodes;
        for (int i = 0; i < nodes.Length; i++) {
            WorldMapNodePlacement placement = nodes[i];
            bool locked = placement.Level != 1;
            context.Scene.Add( new WorldMapLevelNodeInstance(
                SelectNodeSprite( placement.Kind, locked ),
                placement,
                locked ) );
        }
    }

    internal static SpriteRef SelectNodeSprite( WorldMapNodeKind kind, bool locked ) {
        if ( locked ) return GameAssets.Sprites.BubbletaWorldMapLevelSpotLock;
        return kind switch {
            WorldMapNodeKind.Normal => GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            WorldMapNodeKind.Timed => GameAssets.Sprites.BubbletaWorldMapLevelSpotTime,
            WorldMapNodeKind.Moving => GameAssets.Sprites.BubbletaWorldMapLevelSpotMove,
            _ => throw new ArgumentOutOfRangeException( nameof( kind ) )
        };
    }
}
