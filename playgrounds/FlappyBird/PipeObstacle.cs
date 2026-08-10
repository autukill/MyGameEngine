namespace FlappyBirdPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

public readonly record struct PipeSpawnArgs(
    Vector2D Position,
    float Width,
    float Height,
    float Speed,
    bool IsTop);

public sealed class PipeObstacle : GameInstance
{
    private static readonly Vector4 PipeColor = new(0.22f, 0.78f, 0.2f, 1f);
    private static readonly Vector4 PipeHighlight = new(0.42f, 0.96f, 0.31f, 1f);
    private static readonly Vector4 PipeShadow = new(0.05f, 0.3f, 0.08f, 1f);

    private readonly SpriteRef _shape;
    private readonly float _width;
    private readonly float _height;
    private readonly float _speed;
    private readonly bool _isTop;

    public PipeObstacle(SpriteRef shape, in PipeSpawnArgs spawn)
    {
        _shape = shape;
        _width = spawn.Width;
        _height = spawn.Height;
        _speed = spawn.Speed;
        _isTop = spawn.IsTop;
        Position = spawn.Position;
        Collider = CollisionShape2D.Box(_width, _height);
        LocalDrawBounds = Bounds2D.FromCenter(
            Vector2D.Zero,
            new Vector2D(_width + 18f, _height));
        AddTag(GameTags.Obstacle);
        Depth = new LayerDepth(100);
    }

    public override void OnStep(double deltaTime)
    {
        MoveBy(new Vector2D(-_speed * (float)deltaTime, 0f));
        if (Position.X + _width < 0f) DestroySelf();
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        Vector2 center = new(Position.X, Position.Y);
        DrawRect(batch, center + new Vector2(6f, 0f), new Vector2(_width, _height), PipeShadow);
        DrawRect(batch, center, new Vector2(_width, _height), PipeColor);
        DrawRect(batch, center + new Vector2(-_width * 0.27f, 0f),
            new Vector2(_width * 0.14f, _height), PipeHighlight);

        const float lipHeight = 28f;
        Vector2 lipCenter = center + new Vector2(
            0f,
            _isTop
                ? _height * 0.5f - lipHeight * 0.5f
                : -_height * 0.5f + lipHeight * 0.5f);
        DrawRect(batch, lipCenter + new Vector2(6f, 0f),
            new Vector2(_width + 18f, lipHeight), PipeShadow);
        DrawRect(batch, lipCenter, new Vector2(_width + 18f, lipHeight), PipeColor);
        DrawRect(batch, lipCenter + new Vector2(-_width * 0.3f, 0f),
            new Vector2(8f, lipHeight), PipeHighlight);
    }

    private void DrawRect(ISpriteBatch batch, Vector2 center, Vector2 size, Vector4 color) =>
        batch.DrawSpriteExt(_shape, 0f, center, size, 0f, color);
}
