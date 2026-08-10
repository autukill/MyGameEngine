namespace FlappyBirdPlayground;

using GameEngine.Core.Domain.Gameplay;

public static class GamePrefabs
{
    public static readonly PrefabRef<PipeObstacle, PipeSpawnArgs> Pipe =
        new("flappy.pipe");

    public static readonly PrefabRef<ScoreGate, ScoreGateSpawnArgs> ScoreGate =
        new("flappy.score-gate");
}
