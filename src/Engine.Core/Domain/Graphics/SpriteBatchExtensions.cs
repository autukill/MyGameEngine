namespace GameEngine.Core.Domain.Graphics;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>GMS draw_sprite 风格的显式 Batch 便利 API。</summary>
public static class SpriteBatchExtensions
{
    public static void DrawSprite(this ISpriteBatch batch, SpriteRef sprite,
        float subImage, Vector2 position) =>
        batch.DrawSpriteCommand(new SpriteDrawCommand(
            sprite, subImage, position, Vector2.One, 0f, Vector4.One));

    public static void DrawSprite(this ISpriteBatch batch, SpriteRef sprite,
        float subImage, float x, float y) =>
        batch.DrawSprite(sprite, subImage, new Vector2(x, y));

    public static void DrawSpriteExt(this ISpriteBatch batch, SpriteRef sprite,
        float subImage, Vector2 position, Vector2 scale,
        float rotationRadians, Vector4 color) =>
        batch.DrawSpriteCommand(new SpriteDrawCommand(
            sprite, subImage, position, scale, rotationRadians, color));

    /// <summary>以左上角为锚点拉伸到目标尺寸。</summary>
    public static void DrawSpriteStretched(this ISpriteBatch batch, SpriteRef sprite,
        float subImage, Vector2 position, Vector2 size, Vector4? color = null) =>
        batch.DrawSpriteCommand(new SpriteDrawCommand(
            sprite, subImage, position, Vector2.One, 0f, color ?? Vector4.One,
            SizeOverride: size, OriginOverride: Vector2.Zero));
}
