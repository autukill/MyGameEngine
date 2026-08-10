namespace FlappyBirdPlayground;

public static class GameSession
{
    public static int BestScore { get; private set; }

    public static void RecordScore(int score) => BestScore = Math.Max(BestScore, score);
}
