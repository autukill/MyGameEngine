namespace TheGodTheyMade.Simulation.Navigation;

public enum NavigationPathResult
{
    Success,
    InvalidStart,
    InvalidGoal,
    StartBlocked,
    GoalBlocked,
    Unreachable
}

public sealed class NavigationQuery
{
    private readonly int[] _gScore;
    private readonly int[] _cameFrom;
    private readonly byte[] _state;
    private readonly int[] _heap;
    private readonly int[] _heapPosition;
    private int _heapCount;
    private NavigationGrid? _grid;
    private int _goalIndex;

    public NavigationQuery(int maximumCells)
    {
        if (maximumCells <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCells));
        _gScore = new int[maximumCells];
        _cameFrom = new int[maximumCells];
        _state = new byte[maximumCells];
        _heap = new int[maximumCells];
        _heapPosition = new int[maximumCells];
    }

    public NavigationPathResult FindPath(
        NavigationGrid grid,
        GridCell start,
        GridCell goal,
        NavigationPathBuffer output)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();

        if (!grid.Contains(start)) return NavigationPathResult.InvalidStart;
        if (!grid.Contains(goal)) return NavigationPathResult.InvalidGoal;
        if (grid.IsBlocked(start)) return NavigationPathResult.StartBlocked;
        if (grid.IsBlocked(goal)) return NavigationPathResult.GoalBlocked;
        if (grid.CellCount > _state.Length)
            throw new ArgumentException("Grid exceeds this query's maximum cell capacity.", nameof(grid));

        Array.Clear(_state, 0, grid.CellCount);
        Array.Fill(_heapPosition, -1, 0, grid.CellCount);
        _heapCount = 0;
        _grid = grid;
        int startIndex = grid.GetIndex(start);
        _goalIndex = grid.GetIndex(goal);
        _gScore[startIndex] = 0;
        _cameFrom[startIndex] = -1;
        _state[startIndex] = 1;
        HeapAdd(startIndex);

        while (_heapCount > 0)
        {
            int currentIndex = HeapPop();
            if (currentIndex == _goalIndex)
            {
                Reconstruct(grid, currentIndex, output);
                return NavigationPathResult.Success;
            }

            _state[currentIndex] = 2;
            GridCell current = grid.GetCell(currentIndex);
            VisitNeighbor(new GridCell(current.X, current.Y - 1), currentIndex);
            VisitNeighbor(new GridCell(current.X + 1, current.Y), currentIndex);
            VisitNeighbor(new GridCell(current.X, current.Y + 1), currentIndex);
            VisitNeighbor(new GridCell(current.X - 1, current.Y), currentIndex);
        }

        return NavigationPathResult.Unreachable;
    }

    private void VisitNeighbor(GridCell neighbor, int currentIndex)
    {
        NavigationGrid grid = _grid!;
        if (!grid.Contains(neighbor) || grid.IsBlocked(neighbor)) return;

        int neighborIndex = grid.GetIndex(neighbor);
        if (_state[neighborIndex] == 2) return;
        int tentative = _gScore[currentIndex] + 1;
        if (_state[neighborIndex] == 0)
        {
            _gScore[neighborIndex] = tentative;
            _cameFrom[neighborIndex] = currentIndex;
            _state[neighborIndex] = 1;
            HeapAdd(neighborIndex);
            return;
        }

        if (tentative >= _gScore[neighborIndex]) return;
        _gScore[neighborIndex] = tentative;
        _cameFrom[neighborIndex] = currentIndex;
        HeapPromote(_heapPosition[neighborIndex]);
    }

    private void Reconstruct(NavigationGrid grid, int currentIndex, NavigationPathBuffer output)
    {
        output.EnsureCapacity(_gScore[currentIndex] + 1);
        while (currentIndex >= 0)
        {
            output.Add(grid.GetCell(currentIndex));
            currentIndex = _cameFrom[currentIndex];
        }
        output.Reverse();
    }

    private void HeapAdd(int cellIndex)
    {
        int position = _heapCount++;
        _heap[position] = cellIndex;
        _heapPosition[cellIndex] = position;
        HeapPromote(position);
    }

    private int HeapPop()
    {
        int result = _heap[0];
        _heapPosition[result] = -1;
        _heapCount--;
        if (_heapCount > 0)
        {
            int tail = _heap[_heapCount];
            _heap[0] = tail;
            _heapPosition[tail] = 0;
            HeapDemote(0);
        }
        return result;
    }

    private void HeapPromote(int position)
    {
        while (position > 0)
        {
            int parent = (position - 1) / 2;
            if (Compare(_heap[parent], _heap[position]) <= 0) return;
            Swap(parent, position);
            position = parent;
        }
    }

    private void HeapDemote(int position)
    {
        while (true)
        {
            int left = position * 2 + 1;
            if (left >= _heapCount) return;
            int right = left + 1;
            int best = right < _heapCount && Compare(_heap[right], _heap[left]) < 0
                ? right
                : left;
            if (Compare(_heap[position], _heap[best]) <= 0) return;
            Swap(position, best);
            position = best;
        }
    }

    private int Compare(int firstIndex, int secondIndex)
    {
        int firstH = Heuristic(firstIndex);
        int secondH = Heuristic(secondIndex);
        int comparison = (_gScore[firstIndex] + firstH)
            .CompareTo(_gScore[secondIndex] + secondH);
        if (comparison != 0) return comparison;
        comparison = firstH.CompareTo(secondH);
        return comparison != 0 ? comparison : firstIndex.CompareTo(secondIndex);
    }

    private int Heuristic(int cellIndex)
    {
        NavigationGrid grid = _grid!;
        GridCell cell = grid.GetCell(cellIndex);
        GridCell goal = grid.GetCell(_goalIndex);
        return Math.Abs(cell.X - goal.X) + Math.Abs(cell.Y - goal.Y);
    }

    private void Swap(int first, int second)
    {
        int firstCell = _heap[first];
        int secondCell = _heap[second];
        _heap[first] = secondCell;
        _heap[second] = firstCell;
        _heapPosition[firstCell] = second;
        _heapPosition[secondCell] = first;
    }
}
