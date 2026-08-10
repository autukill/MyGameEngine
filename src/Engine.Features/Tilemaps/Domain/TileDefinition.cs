namespace GameEngine.Features.Tilemaps.Domain;

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
