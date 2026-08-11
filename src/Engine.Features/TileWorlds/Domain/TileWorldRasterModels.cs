namespace GameEngine.Features.TileWorlds.Domain;

using GameEngine.Core.Domain.ValueObjects;

public readonly record struct TileWorldRasterSourceFrame
{
    public TileWorldRasterSourceFrame(int width, int height, ReadOnlyMemory<byte> rgbaPixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgbaPixels.Length != checked(width * height * 4))
            throw new ArgumentException("Raster source pixels must be tightly packed RGBA8.", nameof(rgbaPixels));
        Width = width;
        Height = height;
        RgbaPixels = rgbaPixels;
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> RgbaPixels { get; }
}

public interface ITileWorldRasterSource
{
    bool TryResolve(SpriteRef sprite, int subImage, out TileWorldRasterSourceFrame frame);
}

public sealed record TileWorldRasterLayerImage
{
    public TileWorldRasterLayerImage(
        int layerIndex,
        int width,
        int height,
        int gutter,
        byte[] rgbaPixels)
    {
        if (layerIndex < 0) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (gutter is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(gutter));
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        int encodedWidth = checked(width + gutter * 2);
        int encodedHeight = checked(height + gutter * 2);
        if (rgbaPixels.Length != checked(encodedWidth * encodedHeight * 4))
            throw new ArgumentException("Raster image pixels must include the declared Gutter.", nameof(rgbaPixels));
        LayerIndex = layerIndex;
        Width = width;
        Height = height;
        Gutter = gutter;
        RgbaPixels = rgbaPixels;
    }

    public int LayerIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public int Gutter { get; }
    public byte[] RgbaPixels { get; }
    public int EncodedWidth => checked(Width + Gutter * 2);
    public int EncodedHeight => checked(Height + Gutter * 2);
}

public sealed class TileWorldRasterChunkImage
{
    private readonly TileWorldRasterLayerImage[] _layers;

    public TileWorldRasterChunkImage(
        TileWorldChunkKey key,
        IEnumerable<TileWorldRasterLayerImage> layers)
    {
        if (key.Level <= 0) throw new ArgumentOutOfRangeException(nameof(key));
        ArgumentNullException.ThrowIfNull(layers);
        Key = key;
        _layers = layers.OrderBy(layer => layer.LayerIndex).ToArray();
        if (_layers.Length == 0)
            throw new ArgumentException("Raster image Chunk requires at least one non-empty layer.", nameof(layers));
        if (_layers.Select(layer => layer.LayerIndex).Distinct().Count() != _layers.Length)
            throw new ArgumentException("Raster image layer indices must be unique.", nameof(layers));
    }

    public TileWorldChunkKey Key { get; }
    public IReadOnlyList<TileWorldRasterLayerImage> Layers => _layers;
}
