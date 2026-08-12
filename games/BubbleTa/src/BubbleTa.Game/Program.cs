namespace BubbleTa.Game;

using BubbleTa.Game.Content;
using BubbleTa.Game.Home;
using BubbleTa.Game.WorldMap;
using GameEngine.Core.Infrastructure.Windowing;
using GameEngine.Features.ViewportNavigation;
using GameEngine.Hosting;

internal static class Program {
    private static void Main( string[] args ) {
        bool smoke = args.Contains( "--smoke", StringComparer.Ordinal );
        EngineWindowOptions options = (EngineWindowOptions.Default with {
            Title = "天天泡泡TA / BubbleTa",
            Size = new Silk.NET.Maths.Vector2D<int>( 720, 1280 ),
            IsVisible = !smoke,
            VSync = !smoke
        }).WithFixedUpdateRate( 60d );
        WorldMapProgressSnapshot progress = WorldMapProgressSnapshot.NewGame;

        using var game = GameApplication
            .Create( options )
            .UseAudio( new AudioHostingOptions( ForceSilentBackend: smoke ) )
            .UseDefault2DRenderer( renderer => renderer.UseContentCatalog() )
            .AddScene(
                GameScenes.Home,
                GameAssets.Packages.BubbletaHome,
                views => views.ConfigureMain( new SceneCameraState(
                    new System.Numerics.Vector2(
                        HomeSceneLayout.CameraPosition.X,
                        HomeSceneLayout.CameraPosition.Y ) ) ),
                context => HomeSceneDefinition.Configure( context, smoke ) )
            .AddScene(
                GameScenes.WorldMap,
                GameAssets.Packages.BubbletaWorldMap,
                views => views.ConfigureMain(
                    new SceneCameraState( WorldMapSceneLayout.InitialCameraPosition ),
                    navigation: navigation => navigation
                        .Drag( WorldMapSceneLayout.NavigationDrag )
                        .Decelerate()
                        .Bounce( WorldMapSceneLayout.NavigationBounce ) ),
                context => WorldMapSceneDefinition.Configure( context, progress, smoke ) )
            .StartScene( GameScenes.Home )
            .Build();

        game.Run();
    }
}
