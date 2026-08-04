namespace GameEngine.Features.TextureAtlas;

using System.Numerics;

public record PackNode(string Name, int X, int Y, int Width, int Height);

public class ShelfBinPacker
{
    private readonly int _atlasWidth;
    private readonly int _atlasHeight;
    private readonly int _padding;

    private int _currentX = 0;
    private int _currentY = 0;
    private int _shelfHeight = 0;

    public ShelfBinPacker(int atlasWidth = 2048, int atlasHeight = 2048, int padding = 2)
    {
        _atlasWidth = atlasWidth;
        _atlasHeight = atlasHeight;
        _padding = padding;
    }

    /// <summary>
    /// 将一组子图打包并计算 UV 坐标
    /// </summary>
    public Dictionary<string, SpriteRegion> Pack(IEnumerable<(string Name, int Width, int Height)> imageSizes)
    {
        var result = new Dictionary<string, SpriteRegion>();

        // 按高度降序排序（Shelf 算法最佳实践，减少空间浪费）
        var sortedImages = imageSizes.OrderByDescending(img => img.Height).ToList();

        foreach (var img in sortedImages)
        {
            int w = img.Width + _padding * 2;
            int h = img.Height + _padding * 2;

            // 如果当前 Shelf 放不下，切换到下一行 Shelf
            if (_currentX + w > _atlasWidth)
            {
                _currentY += _shelfHeight;
                _currentX = 0;
                _shelfHeight = 0;
            }

            // 图集空间溢出检查
            if (_currentY + h > _atlasHeight)
            {
                throw new InvalidOperationException($"Texture Atlas out of bounds! Exceeded {_atlasWidth}x{_atlasHeight}.");
            }

            // 计算实际像素位置（加上 Padding 防止采样渗色/Bleeding）
            int pixelX = _currentX + _padding;
            int pixelY = _currentY + _padding;

            // 计算 0.0 ~ 1.0 的 UV 坐标
            float u0 = (float)pixelX / _atlasWidth;
            float v0 = (float)pixelY / _atlasHeight;
            float u1 = (float)(pixelX + img.Width) / _atlasWidth;
            float v1 = (float)(pixelY + img.Height) / _atlasHeight;

            var region = new SpriteRegion(
                img.Name,
                new Vector4(u0, v0, u1, v1),
                new Vector2(img.Width, img.Height)
            );

            result.Add(img.Name, region);

            // 更新 Shelf 位置
            _currentX += w;
            if (h > _shelfHeight) _shelfHeight = h;
        }

        return result;
    }
}
