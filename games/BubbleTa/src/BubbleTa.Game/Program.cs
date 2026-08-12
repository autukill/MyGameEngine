namespace BubbleTa.Game;

using BubbleTa.Game.Content;
using BubbleTa.Game.Home;
using GameEngine.Core.Infrastructure.Windowing;
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

        using var game = GameApplication
            .Create( options )
            .UseAudio( new AudioHostingOptions( ForceSilentBackend: smoke ) )
            .UseDefault2DRenderer( renderer => renderer.UseContentCatalog() )
            .AddScene(
                GameScenes.Home,
                GameAssets.Packages.BubbletaHome,
                context => HomeSceneDefinition.Configure( context, smoke ) )
            .AddScene( GameScenes.WorldMap, context => WorldMapPlaceholderScene.Configure( context, smoke ) )
            .StartScene( GameScenes.Home )
            .Build();

        game.Run();
    }
}
