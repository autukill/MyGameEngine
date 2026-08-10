namespace TheGodTheyMade.Simulation.Navigation;

public static class MingzhongNavigation
{
    public const int Width = 48;
    public const int Height = 32;
    public const int TileSize = 32;
    public static readonly GridCell GateBoulder = new(31, 11);

    public static NavigationGrid CreateGrid(bool gateBlocked = true)
    {
        var grid = new NavigationGrid(Width, Height);
        BlockBorder(grid);
        BlockRectangle(grid, 21, 2, 37, 7);   // reservoir
        BlockRectangle(grid, 39, 4, 46, 13);  // old ruin body
        BlockRectangle(grid, 7, 5, 10, 9);    // bell tower body
        BlockRectangle(grid, 3, 11, 6, 14);   // bell household
        BlockRectangle(grid, 7, 15, 10, 18);  // canal household
        BlockRectangle(grid, 4, 20, 7, 23);   // cemetery household
        BlockRectangle(grid, 12, 16, 15, 19); // workshop household
        BlockRectangle(grid, 13, 11, 17, 14); // workshop body
        if (gateBlocked) grid.SetBlocked(GateBoulder, true);
        return grid;
    }

    private static void BlockBorder(NavigationGrid grid)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            grid.SetBlocked(new GridCell(x, 0), true);
            grid.SetBlocked(new GridCell(x, grid.Height - 1), true);
        }
        for (int y = 1; y < grid.Height - 1; y++)
        {
            grid.SetBlocked(new GridCell(0, y), true);
            grid.SetBlocked(new GridCell(grid.Width - 1, y), true);
        }
    }

    private static void BlockRectangle(
        NavigationGrid grid,
        int minX,
        int minY,
        int maxXExclusive,
        int maxYExclusive)
    {
        for (int y = minY; y < maxYExclusive; y++)
            for (int x = minX; x < maxXExclusive; x++)
                grid.SetBlocked(new GridCell(x, y), true);
    }
}
