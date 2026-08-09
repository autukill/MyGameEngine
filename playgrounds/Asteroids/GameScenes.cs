namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Gameplay;

public static class GameScenes
{
    public static readonly SceneRef Main = new("Main");
    public static readonly SceneRef<GameOverArgs> GameOver = new("GameOver");
}

public readonly record struct GameOverArgs(double SurvivalSeconds, int ShotsFired);
