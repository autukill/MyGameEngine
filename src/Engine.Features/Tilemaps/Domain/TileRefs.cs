namespace GameEngine.Features.Tilemaps.Domain;

public readonly record struct TileSetRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);
    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct TileMapRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);
    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct TileId(ushort Value)
{
    public static TileId Empty => default;
    public bool IsEmpty => Value == 0;
    public override string ToString() => Value.ToString();
}

public readonly record struct TileChunkCoordinate(int X, int Y) : IComparable<TileChunkCoordinate>
{
    public int CompareTo(TileChunkCoordinate other)
    {
        int byY = Y.CompareTo(other.Y);
        return byY != 0 ? byY : X.CompareTo(other.X);
    }
}
