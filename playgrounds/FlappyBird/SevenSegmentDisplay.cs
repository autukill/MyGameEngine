namespace FlappyBirdPlayground;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

internal static class SevenSegmentDisplay
{
    private static ReadOnlySpan<byte> SegmentMasks =>
    [
        0b0011_1111, // 0: A B C D E F
        0b0000_0110, // 1: B C
        0b0101_1011, // 2: A B D E G
        0b0100_1111, // 3: A B C D G
        0b0110_0110, // 4: B C F G
        0b0110_1101, // 5: A C D F G
        0b0111_1101, // 6: A C D E F G
        0b0000_0111, // 7: A B C
        0b0111_1111, // 8: all
        0b0110_1111  // 9: A B C D F G
    ];

    public static void DrawNumber(
        ISpriteBatch batch,
        SpriteRef shape,
        int value,
        float centerX,
        float top,
        float height,
        Vector4 color)
    {
        value = Math.Max(0, value);
        Span<int> digits = stackalloc int[10];
        int count = 0;
        do
        {
            digits[count++] = value % 10;
            value /= 10;
        } while (value > 0 && count < digits.Length);

        float digitWidth = height * 0.56f;
        float spacing = height * 0.16f;
        float totalWidth = count * digitWidth + (count - 1) * spacing;
        float left = centerX - totalWidth * 0.5f;
        for (int i = 0; i < count; i++)
        {
            int digit = digits[count - 1 - i];
            DrawDigit(batch, shape, digit, left + i * (digitWidth + spacing), top,
                digitWidth, height, color);
        }
    }

    private static void DrawDigit(
        ISpriteBatch batch,
        SpriteRef shape,
        int digit,
        float left,
        float top,
        float width,
        float height,
        Vector4 color)
    {
        byte mask = SegmentMasks[digit];
        float thickness = height * 0.11f;
        float horizontalWidth = width - thickness;
        float verticalHeight = height * 0.5f - thickness * 1.35f;
        float centerX = left + width * 0.5f;
        float upperY = top + height * 0.25f;
        float lowerY = top + height * 0.75f;
        float leftX = left + thickness * 0.5f;
        float rightX = left + width - thickness * 0.5f;

        if ((mask & (1 << 0)) != 0) Rect(batch, shape, centerX, top + thickness * 0.5f,
            horizontalWidth, thickness, color);
        if ((mask & (1 << 1)) != 0) Rect(batch, shape, rightX, upperY,
            thickness, verticalHeight, color);
        if ((mask & (1 << 2)) != 0) Rect(batch, shape, rightX, lowerY,
            thickness, verticalHeight, color);
        if ((mask & (1 << 3)) != 0) Rect(batch, shape, centerX, top + height - thickness * 0.5f,
            horizontalWidth, thickness, color);
        if ((mask & (1 << 4)) != 0) Rect(batch, shape, leftX, lowerY,
            thickness, verticalHeight, color);
        if ((mask & (1 << 5)) != 0) Rect(batch, shape, leftX, upperY,
            thickness, verticalHeight, color);
        if ((mask & (1 << 6)) != 0) Rect(batch, shape, centerX, top + height * 0.5f,
            horizontalWidth, thickness, color);
    }

    private static void Rect(
        ISpriteBatch batch,
        SpriteRef shape,
        float x,
        float y,
        float width,
        float height,
        Vector4 color) =>
        batch.DrawSpriteExt(shape, 0f, new Vector2(x, y), new Vector2(width, height), 0f, color);
}
