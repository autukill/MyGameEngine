namespace BubbleTa.Game;

using GameEngine.Core.Domain.Gameplay;
using GameEngine.Hosting;

public static class GameScenes
{
    public static readonly SceneRef Home = new("Home");
    public static readonly SceneRef WorldMap = new("WorldMap");
}

internal static class BubbleTaSceneTransitions
{
    public static SceneTransitionOptions Navigation { get; } =
        SceneTransitions.FadeThroughBlack(.18d, .22d);
}
