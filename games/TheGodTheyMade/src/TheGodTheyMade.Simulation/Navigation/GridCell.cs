namespace TheGodTheyMade.Simulation.Navigation;

public readonly record struct GridCell(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}
