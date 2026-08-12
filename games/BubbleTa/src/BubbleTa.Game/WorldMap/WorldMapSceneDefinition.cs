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

        AddLevelNodes( context );
        AddClouds( context, GameAssets.Sprites.BubbletaWorldMapCloudAbove,
            WorldMapSceneLayout.AboveClouds );

        context.Scene.Add( new WorldMapController(
            () => context.Scenes.SwitchTo( GameScenes.Home ) ) );
        if ( smoke ) context.Scene.Add( new WorldMapSmokeProbe( context.Close ) );
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
