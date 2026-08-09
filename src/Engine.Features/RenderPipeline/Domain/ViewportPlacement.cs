namespace GameEngine.Features.RenderPipeline.Domain;

using System.Numerics;

/// <summary>A resolved presentation rectangle and top-left-origin normalized source region.</summary>
public readonly record struct ViewportPlacement(
    int X,
    int Y,
    int Width,
    int Height,
    Vector4 SourceBounds)
{
    public bool Contains(float x, float y) =>
        x >= X && y >= Y && x < X + Width && y < Y + Height;

    public Vector2 ScreenToSource(float x, float y, int sourceWidth, int sourceHeight)
    {
        if (!Contains(x, y))
            throw new ArgumentOutOfRangeException(nameof(x), "Point is outside the fitted Viewport.");
        float normalizedX = (x - X) / Width;
        float normalizedY = (y - Y) / Height;
        return new Vector2(
            (SourceBounds.X + normalizedX * SourceBounds.Z) * sourceWidth,
            (SourceBounds.Y + normalizedY * SourceBounds.W) * sourceHeight);
    }

    public Vector4 ToTextureUvBounds() => new(
        SourceBounds.X,
        1f - SourceBounds.Y,
        SourceBounds.X + SourceBounds.Z,
        1f - SourceBounds.Y - SourceBounds.W);

    public static ViewportPlacement Calculate(
        int sourceWidth,
        int sourceHeight,
        int screenWidth,
        int screenHeight,
        ViewportRect viewport,
        ViewportFitMode fit)
    {
        if (sourceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        if (!Enum.IsDefined(fit)) throw new ArgumentOutOfRangeException(nameof(fit));

        var (slotX, slotY, slotWidth, slotHeight) = viewport.ToPixels(screenWidth, screenHeight);
        if (slotWidth <= 0 || slotHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport), "Viewport resolves to an empty pixel rectangle.");

        if (fit == ViewportFitMode.Stretch)
            return new ViewportPlacement(slotX, slotY, slotWidth, slotHeight, new Vector4(0f, 0f, 1f, 1f));

        double sourceAspect = (double)sourceWidth / sourceHeight;
        double slotAspect = (double)slotWidth / slotHeight;
        if (fit == ViewportFitMode.Contain)
        {
            int width;
            int height;
            if (sourceAspect >= slotAspect)
            {
                width = slotWidth;
                height = Math.Max(1, (int)Math.Round(slotWidth / sourceAspect));
            }
            else
            {
                height = slotHeight;
                width = Math.Max(1, (int)Math.Round(slotHeight * sourceAspect));
            }
            return new ViewportPlacement(
                slotX + (slotWidth - width) / 2,
                slotY + (slotHeight - height) / 2,
                width,
                height,
                new Vector4(0f, 0f, 1f, 1f));
        }

        if (sourceAspect > slotAspect)
        {
            float visibleWidth = (float)(slotAspect / sourceAspect);
            return new ViewportPlacement(
                slotX, slotY, slotWidth, slotHeight,
                new Vector4((1f - visibleWidth) * 0.5f, 0f, visibleWidth, 1f));
        }

        float visibleHeight = (float)(sourceAspect / slotAspect);
        return new ViewportPlacement(
            slotX, slotY, slotWidth, slotHeight,
            new Vector4(0f, (1f - visibleHeight) * 0.5f, 1f, visibleHeight));
    }
}
