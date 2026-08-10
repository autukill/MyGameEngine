namespace FlappyBirdPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.Input;
using GameEngine.Core.Domain.ValueObjects;

public sealed class GameOverCard : GameInstance
{
    private readonly SpriteRef _shape;
    private readonly int _score;
    private readonly Action _close;
    private float _elapsed;

    public GameOverCard(SpriteRef shape, int score, Action close)
    {
        _shape = shape;
        _score = score;
        _close = close;
        TimeMode = InstanceTimeMode.Unscaled;
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }

    public override void OnStep(double deltaTime)
    {
        _elapsed += (float)deltaTime;
        if (ActionPressed(GameInputs.Restart) || ActionPressed(GameInputs.Flap))
            SwitchScene(GameScenes.Main);
        if (KeyPressed(InputKey.Escape)) _close();
    }

    public override void OnDrawGUI(ISpriteBatch batch)
    {
        Rect(batch, new Vector2(480f, 260f), new Vector2(360f, 250f),
            new Vector4(0.04f, 0.08f, 0.12f, 0.92f));
        Rect(batch, new Vector2(480f, 144f), new Vector2(160f, 15f),
            -0.22f, new Vector4(1f, 0.3f, 0.18f, 1f));
        Rect(batch, new Vector2(480f, 144f), new Vector2(160f, 15f),
            0.22f, new Vector4(1f, 0.3f, 0.18f, 1f));

        SevenSegmentDisplay.DrawNumber(
            batch, _shape, _score, 480f, 190f, 74f,
            new Vector4(1f, 0.88f, 0.25f, 1f));
        SevenSegmentDisplay.DrawNumber(
            batch, _shape, GameSession.BestScore, 480f, 302f, 42f,
            new Vector4(0.38f, 0.9f, 1f, 1f));

        float pulse = 0.45f + 0.55f * MathF.Abs(MathF.Sin(_elapsed * 3.5f));
        Rect(batch, new Vector2(480f, 420f), new Vector2(150f, 12f),
            new Vector4(1f, 1f, 1f, pulse));
    }

    private void Rect(ISpriteBatch batch, Vector2 center, Vector2 size, Vector4 color) =>
        Rect(batch, center, size, 0f, color);

    private void Rect(
        ISpriteBatch batch,
        Vector2 center,
        Vector2 size,
        float rotation,
        Vector4 color) =>
        batch.DrawSpriteExt(_shape, 0f, center, size, rotation, color);
}
