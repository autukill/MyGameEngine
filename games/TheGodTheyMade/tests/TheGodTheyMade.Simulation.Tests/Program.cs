namespace TheGodTheyMade.Simulation.Tests;

using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        Run("Grid validation and revision", GridValidationAndRevision);
        Run("Deterministic shortest path", DeterministicShortestPath);
        Run("Blocked and unreachable results", BlockedAndUnreachable);
        Run("Navigation stable allocation", NavigationStableAllocation);
        Run("Mingzhong roster contract", MingzhongRosterContract);
        Run("Village phase boundaries", VillagePhaseBoundaries);
        Run("Village assignments deterministic", VillageAssignmentsDeterministic);
        Run("All work anchors reachable", AllWorkAnchorsReachable);
        Run("Twelve villagers complete ten-minute day", VillagersCompleteTenMinuteDay);
        Console.WriteLine($"TheGodTheyMade Simulation: {_passed} checks passed.");
    }

    private static void GridValidationAndRevision()
    {
        ExpectThrows<ArgumentOutOfRangeException>(() => new NavigationGrid(0, 4));
        var grid = new NavigationGrid(8, 6);
        Check(grid.Revision == 0, "New grid revision should be zero.");
        Check(grid.SetBlocked(new GridCell(3, 2), true), "First block should change grid.");
        Check(grid.Revision == 1, "Changed block should increment revision.");
        Check(!grid.SetBlocked(new GridCell(3, 2), true), "Repeated block should be a no-op.");
        Check(grid.Revision == 1, "No-op block must not increment revision.");
        Check(grid.SetBlocked(new GridCell(3, 2), false), "Unblock should change grid.");
        Check(grid.Revision == 2, "Unblock should increment revision once.");
    }

    private static void DeterministicShortestPath()
    {
        var grid = new NavigationGrid(5, 5);
        var query = new NavigationQuery(grid.CellCount);
        var first = new NavigationPathBuffer(25);
        var second = new NavigationPathBuffer(25);
        NavigationPathResult result = query.FindPath(
            grid, new GridCell(1, 1), new GridCell(3, 3), first);
        Check(result == NavigationPathResult.Success, "Expected a successful path.");
        Check(first.Count == 5, "Expected Manhattan path length including both ends.");
        Check(first[0] == new GridCell(1, 1), "Path must include start.");
        Check(first[^1] == new GridCell(3, 3), "Path must include goal.");

        query.FindPath(grid, new GridCell(1, 1), new GridCell(3, 3), second);
        Check(first.Items.SequenceEqual(second.Items), "Repeated search must be identical.");
    }

    private static void BlockedAndUnreachable()
    {
        var grid = new NavigationGrid(5, 5);
        var query = new NavigationQuery(grid.CellCount);
        var path = new NavigationPathBuffer(25);
        grid.SetBlocked(new GridCell(1, 1), true);
        Check(query.FindPath(grid, new GridCell(1, 1), new GridCell(4, 4), path) ==
              NavigationPathResult.StartBlocked, "Blocked start should be rejected.");
        grid.SetBlocked(new GridCell(1, 1), false);
        for (int y = 0; y < 5; y++) grid.SetBlocked(new GridCell(2, y), true);
        Check(query.FindPath(grid, new GridCell(1, 1), new GridCell(4, 4), path) ==
              NavigationPathResult.Unreachable, "Closed barrier should be unreachable.");
        Check(path.Count == 0, "Failed search must clear output.");
    }

    private static void NavigationStableAllocation()
    {
        var grid = MingzhongNavigation.CreateGrid();
        var query = new NavigationQuery(grid.CellCount);
        var path = new NavigationPathBuffer(grid.CellCount);
        GridCell start = new(5, 14);
        GridCell goal = new(41, 24);
        for (int i = 0; i < 32; i++)
            Check(query.FindPath(grid, start, goal, path) == NavigationPathResult.Success,
                "Warmup path should succeed.");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            query.FindPath(grid, start, goal, path);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Stable navigation should allocate 0 B, allocated {allocated} B.");
    }

    private static void MingzhongRosterContract()
    {
        IReadOnlyList<VillagerDefinition> roster = MingzhongVillage.Roster;
        Check(roster.Count == 12, "Mingzhong must have exactly twelve villagers.");
        Check(roster.Select(v => v.Id.Value).Distinct(StringComparer.Ordinal).Count() == 12,
            "Villager IDs must be unique.");
        Check(roster.Count(v => v.Role == VillagerRole.Farmer) == 2,
            "First slice requires two farmers.");
        Check(roster.All(v => v.ObservationReliability <= 100 &&
                              v.SocialInfluence <= 100 &&
                              v.TraditionBias <= 100),
            "Authored attributes must remain in range.");
    }

    private static void VillagePhaseBoundaries()
    {
        var director = new VillageDirector();
        Check(director.GetPhase(0) == VillagePhase.Dawn, "Day begins at dawn.");
        Check(director.GetPhase(60 * 60 - 1) == VillagePhase.Dawn, "Dawn end boundary.");
        Check(director.GetPhase(60 * 60) == VillagePhase.FirstWork, "First work boundary.");
        Check(director.GetPhase(300 * 60) == VillagePhase.MiddayGathering,
            "Midday boundary.");
        Check(director.GetPhase(570 * 60) == VillagePhase.ReturnHome,
            "Return-home boundary.");
        Check(director.GetPhase(MingzhongVillage.TicksPerDay) == VillagePhase.Dawn,
            "Schedule must loop exactly at one day.");
    }

    private static void VillageAssignmentsDeterministic()
    {
        ulong first = HashAssignments();
        ulong second = HashAssignments();
        Check(first == second, "Ten-minute assignment stream must be deterministic.");

        VillagerDefinition bellKeeper = MingzhongVillage.Roster[0];
        var director = new VillageDirector();
        Check(director.GetAssignment(bellKeeper, 0, true).Kind == VillageTaskKind.RingBell,
            "Bell keeper should ring at dawn.");
        Check(director.GetAssignment(bellKeeper, 570 * 60, true).Kind ==
              VillageTaskKind.ReturnHome, "Bell keeper should return home at night.");
    }

    private static void AllWorkAnchorsReachable()
    {
        NavigationGrid grid = MingzhongNavigation.CreateGrid();
        var query = new NavigationQuery(grid.CellCount);
        var path = new NavigationPathBuffer(grid.CellCount);
        foreach (VillagerDefinition villager in MingzhongVillage.Roster)
        {
            NavigationPathResult result = query.FindPath(grid, villager.Home, villager.Work, path);
            Check(result == NavigationPathResult.Success,
                $"{villager.Id} work anchor should be reachable, got {result}.");
        }
    }

    private static void VillagersCompleteTenMinuteDay()
    {
        ulong first = SimulateVillageDay();
        ulong second = SimulateVillageDay();
        Check(first == second, "Repeated village movement must produce the same state hash.");
    }

    private static ulong SimulateVillageDay()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        NavigationGrid grid = MingzhongNavigation.CreateGrid();
        var query = new NavigationQuery(grid.CellCount);
        var director = new VillageDirector();
        IReadOnlyList<VillagerDefinition> roster = MingzhongVillage.Roster;
        var agents = new NavigationAgent[roster.Count];
        var assignments = new VillageTaskAssignment[roster.Count];
        for (int i = 0; i < agents.Length; i++)
        {
            agents[i] = new NavigationAgent(
                roster[i].Home,
                MingzhongNavigation.TileSize,
                74f,
                grid.CellCount);
            assignments[i] = new VillageTaskAssignment(
                VillageTaskKind.ReturnHome, roster[i].Home, -1);
        }

        ulong hash = offset;
        for (long tick = 0; tick < MingzhongVillage.TicksPerDay; tick++)
        {
            if (tick == 270 * 60)
                grid.SetBlocked(MingzhongNavigation.GateBoulder, false);
            for (int i = 0; i < agents.Length; i++)
            {
                VillageTaskAssignment next = director.GetAssignment(
                    roster[i], tick, grid.IsBlocked(MingzhongNavigation.GateBoulder));
                if (next != assignments[i])
                {
                    assignments[i] = next;
                    NavigationPathResult result = agents[i].SetDestination(
                        query, grid, next.Destination);
                    Check(result == NavigationPathResult.Success,
                        $"{roster[i].Id} could not plan {next.Kind}: {result}.");
                }
                agents[i].Update(1f / MingzhongVillage.TicksPerSecond);
                Check(!grid.IsBlocked(agents[i].CurrentCell),
                    $"{roster[i].Id} entered blocked cell {agents[i].CurrentCell}.");
            }
        }

        for (int i = 0; i < agents.Length; i++)
        {
            Check(agents[i].HasArrived, $"{roster[i].Id} did not finish return-home path.");
            Check(agents[i].CurrentCell == roster[i].Home,
                $"{roster[i].Id} ended at {agents[i].CurrentCell}, expected {roster[i].Home}.");
            hash ^= unchecked((uint)BitConverter.SingleToInt32Bits(agents[i].Position.X));
            hash *= prime;
            hash ^= unchecked((uint)BitConverter.SingleToInt32Bits(agents[i].Position.Y));
            hash *= prime;
        }
        return hash;
    }

    private static ulong HashAssignments()
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        var director = new VillageDirector();
        for (long tick = 0; tick < MingzhongVillage.TicksPerDay; tick++)
        {
            foreach (VillagerDefinition villager in MingzhongVillage.Roster)
            {
                VillageTaskAssignment assignment = director.GetAssignment(villager, tick, true);
                hash ^= (byte)assignment.Kind;
                hash *= prime;
                hash ^= unchecked((uint)assignment.Destination.X);
                hash *= prime;
                hash ^= unchecked((uint)assignment.Destination.Y);
                hash *= prime;
            }
        }
        return hash;
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"[PASS] {name}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
