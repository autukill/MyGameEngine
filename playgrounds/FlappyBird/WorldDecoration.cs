namespace FlappyBirdPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

public sealed class SkyBackdrop : GameInstance
{
    private readonly SpriteRef _shape;
    private readonly float _width;
    private readonly float _height;

    public SkyBackdrop(SpriteRef shape, float width, float height)
    {
        _shape = shape;
        _width = width;
        _height = height;
        Depth = LayerDepth.Background;
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        Draw(batch, new Vector2(_width * 0.2f, _height * 0.2f), new Vector2(150f, 25f),
            new Vector4(1f, 1f, 1f, 0.18f));
        Draw(batch, new Vector2(_width * 0.73f, _height * 0.32f), new Vector2(210f, 30f),
            new Vector4(1f, 1f, 1f, 0.15f));
        Draw(batch, new Vector2(_width * 0.48f, _height * 0.66f), new Vector2(125f, 20f),
            new Vector4(1f, 1f, 1f, 0.1f));
    }

    private void Draw(ISpriteBatch batch, Vector2 center, Vector2 size, Vector4 color) =>
        batch.DrawSpriteExt(_shape, 0f, center, size, 0f, color);
}

public sealed class GroundStrip : GameInstance
{
    private readonly SpriteRef _shape;
    private readonly float _width;
    private readonly float _groundTop;
    private readonly float _worldHeight;

    public GroundStrip(SpriteRef shape, float width, float groundTop, float worldHeight)
    {
        _shape = shape;
        _width = width;
        _groundTop = groundTop;
        _worldHeight = worldHeight;
        Depth = new LayerDepth(-100);
        ViewCulling = InstanceViewCullingMode.AlwaysVisible;
    }

    public override void OnDraw(ISpriteBatch batch)
    {
        float height = _worldHeight - _groundTop;
        batch.DrawSpriteStretched(
            _shape,
            0f,
            new Vector2(0f, _groundTop),
            new Vector2(_width, height),
            new Vector4(0.44f, 0.27f, 0.12f, 1f));
        batch.DrawSpriteStretched(
            _shape,
            0f,
            new Vector2(0f, _groundTop),
            new Vector2(_width, 13f),
            new Vector4(0.78f, 0.9f, 0.22f, 1f));
    }
}
