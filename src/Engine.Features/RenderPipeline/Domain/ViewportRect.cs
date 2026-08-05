namespace GameEngine.Features.RenderPipeline.Domain;

/// <summary>
/// 视口矩形：NDC (0~1) 标准化坐标。
/// 用于分屏、小地图 PIP、宽屏 letterbox 黑边。
/// </summary>
public readonly record struct ViewportRect(float X, float Y, float Width, float Height)
{
    public static ViewportRect FullScreen => new(0f, 0f, 1f, 1f);
    public static ViewportRect TopHalf => new(0f, 0f, 1f, 0.5f);
    public static ViewportRect BottomHalf => new(0f, 0.5f, 1f, 0.5f);
    public static ViewportRect TopRightQuarter => new(0.75f, 0f, 0.25f, 0.25f);

    public (int x, int y, int w, int h) ToPixels(int screenWidth, int screenHeight)
        => (
            (int)(X * screenWidth),
            (int)(Y * screenHeight),
            (int)(Width * screenWidth),
            (int)(Height * screenHeight)
        );
}
