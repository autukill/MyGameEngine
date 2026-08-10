namespace FlappyBirdPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Audio;

public sealed class Bird : GameInstance
{
    private const float Gravity = 1_250f;
    private const float FlapVelocity = -420f;
    private const float MaximumFallVelocity = 720f;
    private const float BirdRadius = 15f;

    private static readonly Vector4 BodyColor = new(1f, 0.82f, 0.12f, 1f);
    private static readonly Vector4 WingColor = new(1f, 0.5f, 0.08f, 1f);
    private static readonly Vector4 BeakColor = new(1f, 0.26f, 0.08f, 1f);

    private readonly SpriteRef _shape;
    private readonly AudioRuntime _audio;
    private readonly AudioClipRef _flapSound;
    private readonly AudioClipRef _scoreSound;
    private readonly AudioClipRef _hitSound;
    private readonly Action _close;
    private readonly float _groundTop;
    private float _verticalVelocity;
    private float _elapsed;
    private bool _dead;

    public bool HasStarted { get; private set; }
    public int Score { get; private set; }

    public Bird(
        SpriteRef shape,
        AudioRuntime audio,
        AudioClipRef flapSound,
        AudioClipRef scoreSound,
        AudioClipRef hitSound,
        Vector2D position,
        float groundTop,
        Action close)
    {
        _shape = shape;
        _audio = audio;
        _flapSound = flapSound;
        _scoreSound = scoreSound;
        _hitSound = hitSound;
        _groundTop = groundTop;
        _close = close;
        Position = position;
        Collider = CollisionShape2D.Circle(BirdRadius);
        LocalDrawBounds = Bounds2D.FromCenter(Vector2D.Zero, new Vector2D(52f, 38f));
    }

    public override void OnStep(double deltaTime)
    {
        _elapsed += (float)deltaTime;
        if (KeyPressed(GameEngine.Core.Domain.Input.InputKey.Escape))
        {
            _close();
            return;
        }

        if (!HasStarted)
        {
            if (ActionPressed(GameInputs.Flap)) Flap();
            return;
        }

        if (ActionPressed(GameInputs.Flap)) Flap();

        float dt = (float)deltaTime;
        _verticalVelocity = MathF.Min(
            MaximumFallVelocity,
            _verticalVelocity + Gravity * dt);
        MoveBy(new Vector2D(0f, _verticalVelocity * dt));
        Rotation = Math.Clamp(_verticalVelocity / MaximumFallVelocity, -0.42f, 1.15f);

        if (FirstCollision(GameTags.Obstacle) is not null ||
            Position.Y - BirdRadius <= 0f ||
            Position.Y + BirdRadius >= _groundTop)
        {
            Die();
            return;
        }

        if (FirstCollision<ScoreGate>() is { } gate)
        {
            Score++;
            Destroy(gate);
            _audio.Play(_scoreSound, AudioPlayOptions.Sfx);
        }
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        Vector2 center = new(Position.X, Position.Y);
        DrawPart(batch, center, new Vector2(42f, 29f), Rotation, BodyColor);

        Vector2 wingCenter = center + Rotate(new Vector2(-8f, 6f), Rotation);
        float wingSwing = HasStarted ? Math.Clamp(-_verticalVelocity / 500f, -0.35f, 0.55f) : 0f;
        DrawPart(batch, wingCenter, new Vector2(22f, 13f), Rotation + wingSwing, WingColor);

        Vector2 beakCenter = center + Rotate(new Vector2(24f, 2f), Rotation);
        DrawPart(batch, beakCenter, new Vector2(13f, 8f), Rotation, BeakColor);

        Vector2 eyeCenter = center + Rotate(new Vector2(11f, -6f), Rotation);
        DrawPart(batch, eyeCenter, new Vector2(8f, 8f), Rotation, Vector4.One);
        DrawPart(batch, eyeCenter + Rotate(new Vector2(2f, 0f), Rotation),
            new Vector2(3f, 3f), Rotation, new Vector4(0.03f, 0.04f, 0.05f, 1f));
    }

    public override void OnDrawGUI(ISpriteBatch batch)
    {
        SevenSegmentDisplay.DrawNumber(
            batch,
            _shape,
            Score,
            480f,
            32f,
            54f,
            Vector4.One);

        if (HasStarted) return;
        float pulse = 0.65f + 0.35f * MathF.Sin(_elapsed * 5f);
        var hint = new Vector4(1f, 0.88f, 0.2f, pulse);
        DrawGuiPart(batch, new Vector2(480f, 350f), new Vector2(86f, 13f), 0f, hint);
        DrawGuiPart(batch, new Vector2(464f, 326f), new Vector2(34f, 10f), -0.65f, hint);
        DrawGuiPart(batch, new Vector2(496f, 326f), new Vector2(34f, 10f), 0.65f, hint);
    }

    private void Flap()
    {
        HasStarted = true;
        _verticalVelocity = FlapVelocity;
        _audio.Play(_flapSound, AudioPlayOptions.Sfx);
    }

    private void Die()
    {
        if (_dead) return;
        _dead = true;
        GameSession.RecordScore(Score);
        _audio.Play(_hitSound, AudioPlayOptions.Sfx);
        SwitchScene(GameScenes.GameOver, new GameOverArgs(Score));
    }

    private void DrawPart(
        ISpriteBatch batch,
        Vector2 center,
        Vector2 size,
        float rotation,
        Vector4 color) =>
        batch.DrawSpriteExt(_shape, 0f, center, size, rotation, color);

    private void DrawGuiPart(
        ISpriteBatch batch,
        Vector2 center,
        Vector2 size,
        float rotation,
        Vector4 color) =>
        batch.DrawSpriteExt(_shape, 0f, center, size, rotation, color);

    private static Vector2 Rotate(Vector2 value, float radians)
    {
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new Vector2(
            value.X * cosine - value.Y * sine,
            value.X * sine + value.Y * cosine);
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("bird.velocity", _verticalVelocity);
        writer.Write("bird.started", HasStarted);
        writer.Write("bird.dead", _dead);
        writer.Write("bird.score", Score);
        writer.Write("bird.groundTop", _groundTop);
    }
}
