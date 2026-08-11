namespace GameEngine.Features.Tilemaps.Domain;

using System.Numerics;
using GameEngine.Core.Domain.ValueObjects;

[Flags]
public enum TileTransform : byte
{
    None = 0,
    FlipX = 1 << 0,
    FlipY = 1 << 1,
    Rotate90 = 1 << 2,
    Rotate180 = 1 << 3,
    Rotate270 = Rotate90 | Rotate180
}

public enum TileCollisionKind : byte
{
    None,
    Solid
}

public static class TileTransformOperations
{
    public static void GetScaleAndRotation(
        TileTransform transform,
        out Vector2 scale,
        out float rotationRadians)
    {
        const TileTransform valid = TileTransform.FlipX | TileTransform.FlipY |
                                    TileTransform.Rotate90 | TileTransform.Rotate180;
        if ((transform & ~valid) != 0)
            throw new ArgumentOutOfRangeException(nameof(transform));

        scale = new Vector2(
            (transform & TileTransform.FlipX) != 0 ? -1f : 1f,
            (transform & TileTransform.FlipY) != 0 ? -1f : 1f);
        rotationRadians = (transform & (TileTransform.Rotate90 | TileTransform.Rotate180)) switch
        {
            TileTransform.Rotate90 => MathF.PI * 0.5f,
            TileTransform.Rotate180 => MathF.PI,
            TileTransform.Rotate270 => MathF.PI * 1.5f,
            _ => 0f
        };
    }
}

public readonly record struct TileCell(TileId Tile, TileTransform Transform = TileTransform.None)
{
    public static TileCell Empty => default;
    public bool IsEmpty => Tile.IsEmpty;
}

public sealed record TileDefinition(
    TileId Id,
    SpriteRef Sprite,
    int SubImage = 0,
    TileCollisionKind Collision = TileCollisionKind.None)
{
    public TileDefinition Validate()
    {
        if (Id.IsEmpty)
            throw new ArgumentException("Tile id 0 is reserved for empty cells.", nameof(Id));
        if (Sprite.IsEmpty)
            throw new ArgumentException("Tile Sprite cannot be empty.", nameof(Sprite));
        if (SubImage < 0)
            throw new ArgumentOutOfRangeException(nameof(SubImage));
        return this;
    }
}
