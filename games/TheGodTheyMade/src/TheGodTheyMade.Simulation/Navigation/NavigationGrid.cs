namespace TheGodTheyMade.Simulation.Navigation;

public sealed class NavigationGrid
{
    private readonly bool[] _blocked;

    public int Width { get; }
    public int Height { get; }
    public int CellCount => _blocked.Length;
    public int Revision { get; private set; }

    public NavigationGrid(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if ((long)width * height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid is too large.");

        Width = width;
        Height = height;
        _blocked = new bool[width * height];
    }

    public bool Contains(GridCell cell) =>
        (uint)cell.X < (uint)Width && (uint)cell.Y < (uint)Height;

    public bool IsBlocked(GridCell cell) =>
        !Contains(cell) || _blocked[GetIndex(cell)];

    public bool SetBlocked(GridCell cell, bool blocked)
    {
        if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
        int index = GetIndex(cell);
        if (_blocked[index] == blocked) return false;
        _blocked[index] = blocked;
        Revision = checked(Revision + 1);
        return true;
    }

    public int GetIndex(GridCell cell)
    {
        if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
        return cell.Y * Width + cell.X;
    }

    public GridCell GetCell(int index)
    {
        if ((uint)index >= (uint)_blocked.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new GridCell(index % Width, index / Width);
    }
}
