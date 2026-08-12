namespace BubbleTa.Game.WorldMap;

using System.Numerics;
using BubbleTa.Game.Content;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Hosting;

internal static class WorldMapSceneDefinition {
    public static void Configure(
        Default2DGameContext context,
        WorldMapProgressSnapshot progress,
        bool smoke ) {
        ArgumentNullException.ThrowIfNull( progress );
        if ( context.Content?.Id != GameAssets.Packages.BubbletaWorldMap.Id )
            throw new InvalidOperationException(
                "BubbleTa WorldMapScene requires its scene-scoped WorldMap content package." );
        if ( context.RenderViews[0].Navigation is null )
            throw new InvalidOperationException(
                "BubbleTa WorldMapScene requires its Scene-owned Viewport navigation." );

        context.Scene.Background = BackgroundConfig.FromColor(
            new Vector4( 108f / 255f, 128f / 255f, 223f / 255f, 1f ) );
        context.SceneAudio.PlayMusic( GameAssets.AudioClips.BubbletaWorldMapBgm );
        var firstSegmentMembers = new List<GameInstance>( 48 );

        AddClouds( context, GameAssets.Sprites.BubbletaWorldMapCloudUnder,
            WorldMapSceneLayout.UnderClouds, firstSegmentMembers );

        AddSegmentMember( context, firstSegmentMembers, new WorldMapIslandInstance(
            GameAssets.Sprites.BubbletaWorldMapIslandUpper,
            WorldMapSceneLayout.IslandUpperPosition ) );
        AddSegmentMember( context, firstSegmentMembers, new WorldMapIslandInstance(
            GameAssets.Sprites.BubbletaWorldMapIslandLower,
            WorldMapSceneLayout.IslandLowerPosition ) );

        AddDecorations( context, firstSegmentMembers );
        var controller = new WorldMapController(
            () => context.Scenes.SwitchTo( GameScenes.Home ) );
        AddLevelNodes( context, progress, controller.RequestSelection, firstSegmentMembers );
        AddClouds( context, GameAssets.Sprites.BubbletaWorldMapCloudAbove,
            WorldMapSceneLayout.AboveClouds, firstSegmentMembers );

        context.Scene.Add( controller );
        var firstSegment = new WorldMapSegmentRuntimeGroup( 0, firstSegmentMembers.ToArray() );
        context.Scene.Add( new WorldMapSegmentVisibilityController(
            context.Camera,
            new WorldMapSegmentVisibility( WorldMapSegmentCatalog.All ),
            [firstSegment],
            context.Scene.RaiseEvent ) );
        context.Scene.Add( new WorldMapSkyTransitionController(
            context.Camera,
            WorldMapSegmentCatalog.All,
            color => context.Scene.Background = BackgroundConfig.FromColor( color ) ) );
        if ( smoke ) context.Scene.Add( new WorldMapSmokeProbe( context.Close ) );
    }

    private static void AddDecorations(
        Default2DGameContext context,
        List<GameInstance> members ) {
        AddSegmentMember( context, members, new WorldMapSmokeInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationSmoke,
            WorldMapSceneLayout.SmokePosition ) );
        AddSegmentMember( context, members, new WorldMapStaticDecorationInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationStone,
            WorldMapSceneLayout.StonePosition,
            19 ) );
        AddSegmentMember( context, members, new WorldMapMushroomInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationMushroom,
            WorldMapSceneLayout.MushroomPosition ) );
        AddSegmentMember( context, members, new WorldMapBirdInstance(
            GameAssets.Sprites.BubbletaWorldMapDecorationBird,
            WorldMapSceneLayout.BirdPosition,
            0xB17D_0001UL ) );
        AddSegmentMember( context, members, new WorldMapLuteaInstance(
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
            AddSegmentMember( context, members,
                new WorldMapPersonInstance( people[i], placements[i] ) );

        foreach ( WorldMapApplePlacement placement in WorldMapSceneLayout.Apples )
            AddSegmentMember( context, members, new WorldMapAppleInstance(
                GameAssets.Sprites.BubbletaWorldMapDecorationApple,
                placement ) );
    }

    private static void AddClouds(
        Default2DGameContext context,
        SpriteRef sprite,
        ReadOnlySpan<WorldMapCloudPlacement> placements,
        List<GameInstance> members ) {
        foreach ( WorldMapCloudPlacement placement in placements )
            AddSegmentMember( context, members,
                new WorldMapCloudInstance( sprite, placement ) );
    }

    private static void AddLevelNodes(
        Default2DGameContext context,
        WorldMapProgressSnapshot progress,
        Action<WorldMapLevelSelectionRequested> requestSelection,
        List<GameInstance> members ) {
        ReadOnlySpan<WorldMapNodePlacement> nodes = WorldMapSceneLayout.FirstIslandNodes;
        for (int i = 0; i < nodes.Length; i++) {
            WorldMapNodePlacement placement = nodes[i];
            WorldMapLevelState state = progress.GetState( placement.Level );
            AddSegmentMember( context, members, new WorldMapLevelNodeInstance(
                SelectNodeSprite( placement.Kind, state ),
                placement,
                state,
                progress.GetStars( placement.Level ),
                screen => context.TryScreenToWorld( screen, out Vector2D world, out _ )
                    ? world
                    : null,
                requestSelection ) );
        }
    }

    private static T AddSegmentMember<T>(
        Default2DGameContext context,
        List<GameInstance> members,
        T instance ) where T : GameInstance {
        context.Scene.Add( instance );
        members.Add( instance );
        return instance;
    }

    internal static SpriteRef SelectNodeSprite( WorldMapNodeKind kind, WorldMapLevelState state ) {
        if ( state == WorldMapLevelState.Locked )
            return GameAssets.Sprites.BubbletaWorldMapLevelSpotLock;
        return kind switch {
            WorldMapNodeKind.Normal => GameAssets.Sprites.BubbletaWorldMapLevelSpot,
            WorldMapNodeKind.Timed => GameAssets.Sprites.BubbletaWorldMapLevelSpotTime,
            WorldMapNodeKind.Moving => GameAssets.Sprites.BubbletaWorldMapLevelSpotMove,
            _ => throw new ArgumentOutOfRangeException( nameof( kind ) )
        };
    }
}
