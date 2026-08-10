namespace TheGodTheyMade.Simulation.Navigation;

using System.Numerics;

public sealed class NavigationAgent
{
    private readonly NavigationPathBuffer _path;
    private int _pathIndex;

    public Vector2 Position { get; private set; }
    public float CellSize { get; }
    public float Speed { get; }
    public int PathIndex => _pathIndex;
    public int PathCount => _path.Count;
    public bool HasArrived => _pathIndex >= _path.Count;

    public NavigationAgent(
        GridCell start,
        float cellSize,
        float speed,
        int pathCapacity = 64)
    {
        if (!float.IsFinite(cellSize) || cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        if (!float.IsFinite(speed) || speed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(speed));
        CellSize = cellSize;
        Speed = speed;
        Position = CellCenter(start);
        _path = new NavigationPathBuffer(pathCapacity);
    }

    public GridCell CurrentCell => new(
        (int)MathF.Floor(Position.X / CellSize),
        (int)MathF.Floor(Position.Y / CellSize));

    public NavigationPathResult SetDestination(
        NavigationQuery query,
        NavigationGrid grid,
        GridCell destination)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(grid);
        NavigationPathResult result = query.FindPath(grid, CurrentCell, destination, _path);
        _pathIndex = result == NavigationPathResult.Success
            ? Math.Min(1, _path.Count)
            : 0;
        return result;
    }

    public void Update(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        float remaining = Speed * deltaTime;
        while (remaining > 0f && _pathIndex < _path.Count)
        {
            Vector2 target = CellCenter(_path[_pathIndex]);
            Vector2 delta = target - Position;
            float distance = delta.Length();
            if (distance <= 0.001f)
            {
                Position = target;
                _pathIndex++;
                continue;
            }
            if (distance <= remaining)
            {
                Position = target;
                remaining -= distance;
                _pathIndex++;
                continue;
            }
            Position += delta / distance * remaining;
            remaining = 0f;
        }
    }

    private Vector2 CellCenter(GridCell cell) => new(
        (cell.X + 0.5f) * CellSize,
        (cell.Y + 0.5f) * CellSize);
}
