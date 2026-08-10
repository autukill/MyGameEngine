namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

public sealed class GameOverMarker : GameInstance
{
    public double SurvivalSeconds { get; }
    public int ShotsFired { get; }
    public int Score { get; }

    public GameOverMarker(SpriteRef sprite, Vector2D position, GameOverArgs args)
    {
        Sprite = sprite;
        Position = position;
        Color = new(1f, 0.3f, 0.2f, 1f);
        SurvivalSeconds = args.SurvivalSeconds;
        ShotsFired = args.ShotsFired;
        Score = args.Score;
    }

    public override void OnStep(double deltaTime)
    {
        RotateBy((float)deltaTime);
        if (ActionPressed(GameInputs.Restart))
            SwitchScene(GameScenes.Main);
    }
}
