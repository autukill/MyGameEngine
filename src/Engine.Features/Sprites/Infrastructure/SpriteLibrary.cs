namespace GameEngine.Features.Sprites.Infrastructure;

using System.Numerics;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 手动注册的 Sprite 资源库。纹理句柄由外部拥有，本类型只保存借用引用与帧元数据。
/// </summary>
public sealed class SpriteLibrary : ISpriteResolver
{
    private sealed record Entry(
        uint TextureHandle,
        SpriteMetadata Metadata,
        Vector4[] Frames);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public SpriteRef RegisterSingle(
        string name,
        uint textureHandle,
        Vector2 size,
        Vector2 origin) =>
        RegisterFrames(name, textureHandle, size, origin,
            new[] { new Vector4(0f, 0f, 1f, 1f) }, framesPerSecond: 0f);

    public SpriteRef RegisterFrames(
        string name,
        uint textureHandle,
        Vector2 size,
        Vector2 origin,
        ReadOnlySpan<Vector4> frameUvBounds,
        float framesPerSecond = 0f)
    {
        ValidateCommon(name, textureHandle, size, origin, framesPerSecond);
        if (frameUvBounds.IsEmpty)
            throw new ArgumentException("A sprite must contain at least one frame.", nameof(frameUvBounds));
        if (_entries.ContainsKey(name))
            throw new ArgumentException($"Sprite '{name}' is already registered.", nameof(name));

        var frames = frameUvBounds.ToArray();
        for (int i = 0; i < frames.Length; i++)
            ValidateUv(frames[i], nameof(frameUvBounds));

        var metadata = new SpriteMetadata(size, origin, frames.Length, framesPerSecond);
        _entries.Add(name, new Entry(textureHandle, metadata, frames));
        return new SpriteRef(name);
    }

    public SpriteRef RegisterGrid(
        string name,
        uint textureHandle,
        Vector2 textureSize,
        Vector2 frameSize,
        Vector2 origin,
        int frameCount,
        float framesPerSecond = 0f)
    {
        ValidateSize(textureSize, nameof(textureSize));
        ValidateSize(frameSize, nameof(frameSize));
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count must be positive.");

        int columns = (int)MathF.Floor(textureSize.X / frameSize.X);
        int rows = (int)MathF.Floor(textureSize.Y / frameSize.Y);
        if (columns <= 0 || rows <= 0 || frameCount > columns * rows)
            throw new ArgumentException("Frame count exceeds the supplied texture grid.", nameof(frameCount));

        var frames = new Vector4[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            int column = i % columns;
            int row = i / columns;
            float x1 = column * frameSize.X / textureSize.X;
            float y1 = row * frameSize.Y / textureSize.Y;
            float x2 = (column + 1) * frameSize.X / textureSize.X;
            float y2 = (row + 1) * frameSize.Y / textureSize.Y;
            frames[i] = new Vector4(x1, y1, x2, y2);
        }

        return RegisterFrames(name, textureHandle, frameSize, origin, frames, framesPerSecond);
    }

    public bool TryGetMetadata(SpriteRef sprite, out SpriteMetadata metadata)
    {
        if (!sprite.IsEmpty && _entries.TryGetValue(sprite.Name, out var entry))
        {
            metadata = entry.Metadata;
            return true;
        }
        metadata = default;
        return false;
    }

    public bool TryResolve(SpriteRef sprite, int subImage, out ResolvedSpriteFrame frame)
    {
        if (!sprite.IsEmpty && _entries.TryGetValue(sprite.Name, out var entry))
        {
            int index = NormalizeFrame(subImage, entry.Frames.Length);
            frame = new ResolvedSpriteFrame(
                entry.TextureHandle,
                entry.Metadata.Size,
                entry.Metadata.Origin,
                entry.Frames[index]);
            return true;
        }
        frame = default;
        return false;
    }

    public bool Remove(SpriteRef sprite) =>
        !sprite.IsEmpty && _entries.Remove(sprite.Name);

    public void Clear() => _entries.Clear();

    private static int NormalizeFrame(int frame, int count)
    {
        int normalized = frame % count;
        return normalized < 0 ? normalized + count : normalized;
    }

    private static void ValidateCommon(
        string name, uint textureHandle, Vector2 size, Vector2 origin, float framesPerSecond)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sprite name cannot be empty.", nameof(name));
        if (textureHandle == 0)
            throw new ArgumentOutOfRangeException(nameof(textureHandle), "Texture handle must be non-zero.");
        ValidateSize(size, nameof(size));
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
            throw new ArgumentException("Origin must be finite.", nameof(origin));
        if (!float.IsFinite(framesPerSecond) || framesPerSecond < 0f)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "FPS must be finite and non-negative.");
    }

    private static void ValidateSize(Vector2 size, string paramName)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0f || size.Y <= 0f)
            throw new ArgumentOutOfRangeException(paramName, "Size must be finite and positive.");
    }

    private static void ValidateUv(Vector4 uv, string paramName)
    {
        if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y) ||
            !float.IsFinite(uv.Z) || !float.IsFinite(uv.W) ||
            uv.X < 0f || uv.Y < 0f || uv.Z > 1f || uv.W > 1f ||
            uv.X >= uv.Z || uv.Y >= uv.W)
            throw new ArgumentException("UV bounds must be finite, ordered, and inside [0,1].", paramName);
    }
}
