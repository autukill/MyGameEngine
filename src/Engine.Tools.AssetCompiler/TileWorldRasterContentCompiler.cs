namespace GameEngine.Tools.AssetCompiler;

using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TileWorlds.Domain;
using Imazen.WebP;

internal sealed record TileWorldRasterTextureInput(
    TextureAssetDefinition Definition,
    string Path);

internal sealed class TileWorldRasterSpriteSource : ITileWorldRasterSource
{
    private sealed class TextureSource(
        TileWorldRasterTextureInput input,
        IImageDecoder decoder)
    {
        private DecodedImage? _decoded;
        public TextureAssetDefinition Definition => input.Definition;

        public DecodedImage Decode()
        {
            if (_decoded is { } decoded) return decoded;
            using var stream = File.OpenRead(input.Path);
            _decoded = decoder.Decode(stream);
            return _decoded.Value;
        }
    }

    private readonly IReadOnlyDictionary<string, SpriteAssetDefinition> _sprites;
    private readonly Dictionary<string, TextureSource> _textures;
    private readonly Dictionary<(string Name, int Frame), TileWorldRasterSourceFrame> _frames = [];

    public TileWorldRasterSpriteSource(
        IReadOnlyDictionary<string, SpriteAssetDefinition> sprites,
        IReadOnlyDictionary<string, TileWorldRasterTextureInput> textures,
        IImageDecoder decoder)
    {
        _sprites = sprites ?? throw new ArgumentNullException(nameof(sprites));
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(decoder);
        _textures = textures.ToDictionary(
            pair => pair.Key,
            pair => new TextureSource(pair.Value, decoder),
            StringComparer.Ordinal);
    }

    public bool TryResolve(SpriteRef sprite, int subImage, out TileWorldRasterSourceFrame frame)
    {
        if (sprite.IsEmpty || !_sprites.TryGetValue(sprite.Name, out SpriteAssetDefinition? definition))
        {
            frame = default;
            return false;
        }

        int frameCount = GetFrameCount(definition);
        int frameIndex = subImage % frameCount;
        if (frameIndex < 0) frameIndex += frameCount;
        if (_frames.TryGetValue((sprite.Name, frameIndex), out frame)) return true;

        ResolveFrame(definition, frameIndex, out string textureName, out PixelRectI? declaredRect);
        if (!_textures.TryGetValue(textureName, out TextureSource? texture))
        {
            frame = default;
            return false;
        }
        DecodedImage decoded = texture.Decode();
        PixelRectI rect = declaredRect ?? new PixelRectI(0, 0, decoded.Width, decoded.Height);
        ValidateRect(sprite.Name, textureName, rect, decoded);
        byte[] pixels = Crop(decoded, rect);
        frame = new TileWorldRasterSourceFrame(rect.Width, rect.Height, pixels);
        _frames.Add((sprite.Name, frameIndex), frame);
        return true;
    }

    private int GetFrameCount(SpriteAssetDefinition sprite) => sprite.Layout switch
    {
        SpriteAssetLayout.Single => 1,
        SpriteAssetLayout.Grid => sprite.FrameCount
            ?? throw new InvalidDataException($"Grid Sprite '{sprite.Name}' has no frame count."),
        SpriteAssetLayout.Frames when sprite.Frames.Count > 0 => sprite.Frames.Count,
        SpriteAssetLayout.Frames => throw new InvalidDataException($"Frames Sprite '{sprite.Name}' has no frames."),
        _ => throw new InvalidDataException($"Sprite '{sprite.Name}' has an unsupported layout.")
    };

    private void ResolveFrame(
        SpriteAssetDefinition sprite,
        int frameIndex,
        out string textureName,
        out PixelRectI? rect)
    {
        switch (sprite.Layout)
        {
            case SpriteAssetLayout.Single:
                textureName = sprite.TextureName!;
                rect = sprite.SourceRect;
                return;
            case SpriteAssetLayout.Grid:
                textureName = sprite.TextureName!;
                if (!_textures.TryGetValue(textureName, out TextureSource? texture))
                    throw new InvalidDataException(
                        $"Grid Sprite '{sprite.Name}' references unavailable Texture '{textureName}'.");
                DecodedImage image = texture.Decode();
                PixelSizeI frameSize = sprite.FrameSize
                    ?? throw new InvalidDataException($"Grid Sprite '{sprite.Name}' has no frame size.");
                int columns = image.Width / frameSize.Width;
                int rows = image.Height / frameSize.Height;
                int count = GetFrameCount(sprite);
                if (columns <= 0 || rows <= 0 || count > checked(columns * rows))
                    throw new InvalidDataException(
                        $"Grid Sprite '{sprite.Name}' exceeds Texture '{textureName}'.");
                rect = new PixelRectI(
                    frameIndex % columns * frameSize.Width,
                    frameIndex / columns * frameSize.Height,
                    frameSize.Width,
                    frameSize.Height);
                return;
            case SpriteAssetLayout.Frames:
                SpriteAssetFrameDefinition source = sprite.Frames[frameIndex];
                textureName = source.TextureName ?? sprite.TextureName
                    ?? throw new InvalidDataException(
                        $"Sprite '{sprite.Name}' frame {frameIndex} has no Texture.");
                rect = source.SourceRect;
                return;
            default:
                throw new InvalidDataException($"Sprite '{sprite.Name}' has an unsupported layout.");
        }
    }

    private static void ValidateRect(
        string spriteName,
        string textureName,
        PixelRectI rect,
        DecodedImage image)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.Right > image.Width || rect.Bottom > image.Height)
            throw new InvalidDataException(
                $"Sprite '{spriteName}' frame exceeds Texture '{textureName}'.");
    }

    private static byte[] Crop(DecodedImage image, PixelRectI rect)
    {
        var result = new byte[checked(rect.Width * rect.Height * 4)];
        int sourceStride = checked(image.Width * 4);
        int targetStride = checked(rect.Width * 4);
        for (int y = 0; y < rect.Height; y++)
        {
            Buffer.BlockCopy(
                image.RgbaPixels,
                checked((rect.Y + y) * sourceStride + rect.X * 4),
                result,
                y * targetStride,
                targetStride);
        }
        return result;
    }
}

internal static class TileWorldLosslessWebpEncoder
{
    public static byte[] Encode(TileWorldRasterLayerImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Encode(image.EncodedWidth, image.EncodedHeight, image.RgbaPixels);
    }

    public static byte[] Encode(int width, int height, byte[] rgbaPixels)
        => Encode(width, height, rgbaPixels, losslessPreset: 9);

    public static byte[] EncodeForStreamingLod(int width, int height, byte[] rgbaPixels)
        => Encode(width, height, rgbaPixels, losslessPreset: 4);

    private static byte[] Encode(
        int width,
        int height,
        byte[] rgbaPixels,
        int losslessPreset)
    {
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgbaPixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA pixel length does not match dimensions.", nameof(rgbaPixels));
        using var destination = new MemoryStream();
        var config = new WebPEncoderConfig()
            .SetLosslessPreset(losslessPreset)
            .SetExact();
        WebPEncoder.Encode(
            rgbaPixels,
            width,
            height,
            checked(width * 4),
            WebPPixelFormat.Rgba,
            config,
            destination);
        return destination.ToArray();
    }
}
