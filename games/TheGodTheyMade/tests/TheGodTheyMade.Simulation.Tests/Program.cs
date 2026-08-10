namespace TheGodTheyMade.Simulation.Tests;

using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;
using TheGodTheyMade.Simulation.Beliefs;
using TheGodTheyMade.Simulation.Familiar;
using TheGodTheyMade.Simulation.Scenario;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        try
        {
            RunAll();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[FAIL] {exception}");
            Environment.ExitCode = 1;
        }
    }

    private static void RunAll()
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
        Run("Observable world initial contract", ObservableWorldInitialContract);
        Run("Commands use deterministic tick boundary", CommandsUseDeterministicTickBoundary);
        Run("Rain withers and recovers covered crop", RainWithersAndRecoversCrop);
        Run("Reservoir gate and canal transition", ReservoirGateAndCanalTransition);
        Run("Observation channels respect sight and hearing", ObservationChannelsRespectSightAndHearing);
        Run("Observation memory keeps strongest recent evidence", ObservationMemoryCapacity);
        Run("Observable world deterministic hash", ObservableWorldDeterministicHash);
        Run("Observable world stable allocation", ObservableWorldStableAllocation);
        Run("Belief thresholds and authored priors", BeliefThresholdsAndPriors);
        Run("Observed cause effect supports hypothesis", ObservedCauseEffectSupportsHypothesis);
        Run("Expired cause creates contradiction", ExpiredCauseCreatesContradiction);
        Run("Unseen event cannot become belief", UnseenEventCannotBecomeBelief);
        Run("Gathering establishes public doctrine", GatheringEstablishesPublicDoctrine);
        Run("Belief changes village behavior", BeliefChangesVillageBehavior);
        Run("Belief script deterministic and allocation stable", BeliefDeterministicAndStable);
        Run("Familiar situation priority and ape affordances", FamiliarSituationAndAffordances);
        Run("Familiar demonstration and bounded Q update", FamiliarDemonstrationAndReward);
        Run("Illegal action and failure cooldown are hard filters", FamiliarHardFilters);
        Run("Familiar praise stop and explainable correction", FamiliarPraiseStopAndCorrection);
        Run("Familiar snapshot restores next decision", FamiliarSnapshotRestoresNextDecision);
        Run("Familiar training deterministic and allocation stable", FamiliarTrainingDeterministicAndStable);
        Run("Island no-input route remains completable", IslandNoInputRouteCompletes);
        Run("Wet ruin and funeral choice produce distinct mural", IslandChoicesProduceDistinctMural);
        Run("Island chapter deterministic and stable after completion", IslandChapterDeterministicAndStable);
        Run("Gameplay command recording validates protocol", GameplayCommandRecordingValidatesProtocol);
        Run("Gameplay command journal reproduces world", GameplayCommandJournalReproducesWorld);
        Run("Gameplay command journal reproduces familiar feedback", GameplayCommandJournalReproducesFamiliarFeedback);
        Run("Gameplay command playback rejects missed or divergent command", GameplayCommandPlaybackRejectsDivergence);
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

    private static void ObservableWorldInitialContract()
    {
        var world = NewWorld();
        Check(world.Tick == 0, "World should begin at tick zero.");
        Check(world.GodIntent == 2, "World should begin with two God Intent charges.");
        Check(world.Reservoir == ReservoirLevel.Low, "Reservoir should begin low.");
        Check(world.Gate == GateState.Blocked, "Gate should begin blocked.");
        Check(world.Canal == CanalState.Dry, "Canal should begin dry.");
        Check(world.FieldCount == 3, "Mingzhong should have three fields.");
        Check(world.GetField(0).Moisture == 28 &&
              world.GetField(1).Moisture == 22 &&
              world.GetField(2).Moisture == 18,
            "Field moisture must match the first-playable contract.");
    }

    private static void CommandsUseDeterministicTickBoundary()
    {
        var world = NewWorld();
        Check(!world.TryApply(MingzhongCommand.Rain(1, new GridCell(41, 24))),
            "A future-tick command must not execute early.");
        Check(world.TryApply(MingzhongCommand.Rain(0, new GridCell(41, 24))),
            "A command at the current fixed tick should execute.");
        Check(!world.TryApply(MingzhongCommand.Rain(0, new GridCell(29, 22))),
            "Overlapping rain commands should be rejected deterministically.");
        Check(world.GodIntent == 1, "Only the accepted rain command may spend intent.");
        Check(world.Observations[^1].Kind == ObservationKind.RainStarted,
            "Accepted rain should publish its logical result, not pointer pixels.");
    }

    private static void RainWithersAndRecoversCrop()
    {
        var world = NewWorld();
        Advance(world, 42 * MingzhongVillage.TicksPerSecond + 1);
        FieldSnapshot east = world.GetField(2);
        Check(east.Withered, "The initially dry east field should wither at the scripted boundary.");
        Check(world.TryApply(MingzhongCommand.Rain(world.Tick, east.Center, 4)),
            "Rain should be accepted over the east field.");
        Advance(world, MingzhongWorldSimulation.RainDurationTicks);
        east = world.GetField(2);
        Check(!east.Withered && east.Moisture >= 25,
            "Covered rain should recover a withered field after crossing the moisture threshold.");
        Check(world.Observations.ToArray().Any(o => o.Kind == ObservationKind.CropWithered),
            "Withering should be published as a world observation.");
        Check(world.Observations.ToArray().Any(o => o.Kind == ObservationKind.CropRecovered),
            "Recovery should be published as a world observation.");
        Check(world.Observations.ToArray().Any(o => o.Kind == ObservationKind.RainEnded),
            "A finite local rain should publish its end.");
    }

    private static void ReservoirGateAndCanalTransition()
    {
        var world = NewWorld();
        Check(world.TryApply(MingzhongCommand.Rain(world.Tick, new GridCell(29, 4), 6)),
            "Rain should cover the reservoir.");
        Check(world.TryApply(MingzhongCommand.OpenGate(world.Tick)),
            "Gate opening is an independent final gameplay command.");
        Advance(world, MingzhongWorldSimulation.RainDurationTicks);
        Check(world.Reservoir == ReservoirLevel.Ready,
            $"Reservoir should become ready, units={world.ReservoirUnits}.");
        Check(world.Canal == CanalState.Filling, "Ready water behind an open gate should fill the canal.");
        Advance(world, MingzhongWorldSimulation.CanalFillTicks);
        Check(world.Canal == CanalState.Flowing, "Canal should become flowing after its fixed delay.");
    }

    private static void ObservationChannelsRespectSightAndHearing()
    {
        VillagerDefinition observer = MingzhongVillage.Roster[0] with
        {
            Home = new GridCell(0, 0),
            Work = new GridCell(0, 0)
        };
        var world = new MingzhongWorldSimulation(
            new[] { observer },
            cell => cell == new GridCell(1, 0));
        world.Publish(ObservationKind.CropWithered, "field.test", null, new GridCell(2, 0));
        Check(world.GetMemory(observer.Id).Count == 0,
            "A visual event behind an opaque cell must not enter memory.");
        world.Publish(ObservationKind.BellRang, "bell.test", null, new GridCell(2, 0));
        Check(world.GetMemory(observer.Id).Count == 1,
            "An auditory event in range should pass through visual obstruction.");
        world.Publish(ObservationKind.VillagerInjured, "hazard", observer.Id.Value, new GridCell(47, 31));
        Check(world.GetMemory(observer.Id).Count == 2,
            "A direct event should reach its participant independent of distance.");
    }

    private static void ObservationMemoryCapacity()
    {
        VillagerDefinition observer = MingzhongVillage.Roster[0] with
        {
            Home = MingzhongVillage.Bell,
            Work = MingzhongVillage.Bell
        };
        var world = new MingzhongWorldSimulation(new[] { observer });
        for (int i = 0; i < 40; i++)
        {
            world.Publish(ObservationKind.BellRang, "bell.test", null, MingzhongVillage.Bell);
            world.AdvanceTick();
        }
        VillagerObservationMemory memory = world.GetMemory(observer.Id);
        Check(memory.Count == VillagerObservationMemory.Capacity,
            "Observation memory must remain bounded.");
        Check(memory[0].Id.Value == 9 && memory[VillagerObservationMemory.Capacity - 1].Id.Value == 40,
            "Equal-salience overflow should evict the oldest observations first.");
    }

    private static void ObservableWorldDeterministicHash()
    {
        ulong first = SimulateObservableWorld();
        ulong second = SimulateObservableWorld();
        Check(first == second, "The same logical commands must produce the same observable world hash.");
    }

    private static void ObservableWorldStableAllocation()
    {
        var world = NewWorld();
        Advance(world, 8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Advance(world, 1024);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Stable world ticks should allocate 0 B, allocated {allocated} B.");
    }

    private static ulong SimulateObservableWorld()
    {
        var world = NewWorld();
        world.TryApply(MingzhongCommand.Rain(0, new GridCell(29, 4)));
        world.TryApply(MingzhongCommand.OpenGate(0));
        for (int i = 0; i < 8 * 60 * MingzhongVillage.TicksPerSecond; i++)
        {
            if (world.Tick == 7L * 60 * MingzhongVillage.TicksPerSecond + 10)
                world.TryApply(MingzhongCommand.Rain(world.Tick, new GridCell(41, 24)));
            world.AdvanceTick();
        }
        return world.ComputeStateHash();
    }

    private static MingzhongWorldSimulation NewWorld()
    {
        NavigationGrid navigation = MingzhongNavigation.CreateGrid();
        return new MingzhongWorldSimulation(MingzhongVillage.Roster, navigation.IsBlocked);
    }

    private static void Advance(MingzhongWorldSimulation world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.AdvanceTick();
    }

    private static void BeliefThresholdsAndPriors()
    {
        Check(BeliefThresholds.Classify(-201) == BeliefConviction.Opposed, "Opposition boundary.");
        Check(BeliefThresholds.Classify(-200) == BeliefConviction.Undecided, "Undecided boundary.");
        Check(BeliefThresholds.Classify(100) == BeliefConviction.Suspected, "Suspicion boundary.");
        Check(BeliefThresholds.Classify(300) == BeliefConviction.Believed, "Belief boundary.");
        Check(BeliefThresholds.Classify(450) == BeliefConviction.Advocated, "Advocacy boundary.");

        var beliefs = new BeliefSimulation(MingzhongVillage.Roster);
        BeliefHypothesisKey key = BellRainKey();
        Check(beliefs.GetHypothesis(MingzhongVillage.Roster[0].Id, key)?.Score == 120,
            "Cen should begin with the authored bell-rain prior.");
        Check(beliefs.GetHypothesis(MingzhongVillage.Roster[6].Id, key)?.Score == 120,
            "Mian should begin with the authored bell-rain prior.");
        Check(beliefs.GetHypothesis(MingzhongVillage.Roster[1].Id, key) is null,
            "Other villagers must not receive an omniscient prior.");
    }

    private static void ObservedCauseEffectSupportsHypothesis()
    {
        (MingzhongWorldSimulation world, BeliefSimulation beliefs, VillagerDefinition observer) =
            NewSingleObserverBeliefWorld("cen_bellkeeper");
        world.Publish(ObservationKind.BellRang, "bell", null, observer.Home);
        world.Publish(ObservationKind.RainStarted, "rain", null, observer.Home);
        beliefs.Update(world);
        BeliefHypothesisSnapshot hypothesis = beliefs.GetHypothesis(observer.Id, BellRainKey())!.Value;
        Check(hypothesis.Score > 120 && hypothesis.SupportingEvidence == 1,
            "An observed effect inside the window should support the prior hypothesis.");
        Check(hypothesis.Contradictions == 0, "A resolved cause must not later contradict itself.");
    }

    private static void ExpiredCauseCreatesContradiction()
    {
        (MingzhongWorldSimulation world, BeliefSimulation beliefs, VillagerDefinition observer) =
            NewSingleObserverBeliefWorld("cen_bellkeeper");
        world.Publish(ObservationKind.BellRang, "bell", null, observer.Home);
        beliefs.Update(world);
        for (int i = 0; i <= 8 * MingzhongVillage.TicksPerSecond + 1; i++)
        {
            world.AdvanceTick();
            beliefs.Update(world);
        }
        BeliefHypothesisSnapshot hypothesis = beliefs.GetHypothesis(observer.Id, BellRainKey())!.Value;
        Check(hypothesis.Score < 120 && hypothesis.Contradictions == 1,
            "An observed cause without its effect should become one bounded contradiction.");
    }

    private static void UnseenEventCannotBecomeBelief()
    {
        VillagerDefinition observer = MingzhongVillage.Roster[1] with
        {
            Home = new GridCell(0, 0),
            Work = new GridCell(0, 0)
        };
        var world = new MingzhongWorldSimulation(new[] { observer }, cell => cell == new GridCell(1, 0));
        var beliefs = new BeliefSimulation(new[] { observer });
        world.Publish(ObservationKind.CropWithered, "field", null, new GridCell(2, 0));
        world.Publish(ObservationKind.RainStarted, "rain", null, new GridCell(20, 20));
        beliefs.Update(world);
        var key = new BeliefHypothesisKey(ObservationKind.CropWithered, ObservationKind.RainStarted);
        Check(world.GetMemory(observer.Id).Count == 0, "Observer fixture should perceive neither event.");
        Check(beliefs.GetHypothesis(observer.Id, key) is null,
            "The belief layer must never read the omniscient world log.");
    }

    private static void GatheringEstablishesPublicDoctrine()
    {
        VillagerDefinition[] villagers = MingzhongVillage.Roster.Take(3)
            .Select(v => v with { Home = MingzhongVillage.Bell, Work = MingzhongVillage.Bell })
            .ToArray();
        var world = new MingzhongWorldSimulation(villagers);
        var beliefs = new BeliefSimulation(villagers);
        for (int repetition = 0; repetition < 2; repetition++)
        {
            world.Publish(ObservationKind.BellRang, "bell", null, MingzhongVillage.Bell);
            world.Publish(ObservationKind.RainStarted, "rain", null, MingzhongVillage.Bell);
            beliefs.Update(world);
            world.AdvanceTick();
        }
        beliefs.ConductGathering(world.Tick);
        PublicDoctrine? established = beliefs.Doctrine;
        Check(established is { } && established.Value.Key == BellRainKey(),
            "An advocate and two believing responders should establish a doctrine.");
        Check(established!.Value.Responders >= 2, "Public doctrine requires at least two responders.");
    }

    private static void BeliefChangesVillageBehavior()
    {
        (MingzhongWorldSimulation world, BeliefSimulation beliefs, VillagerDefinition observer) =
            NewSingleObserverBeliefWorld("cen_bellkeeper");
        for (int repetition = 0; repetition < 2; repetition++)
        {
            world.Publish(ObservationKind.BellRang, "bell", null, observer.Home);
            world.Publish(ObservationKind.RainStarted, "rain", null, observer.Home);
            beliefs.Update(world);
            world.AdvanceTick();
        }
        VillageBeliefBehavior behavior = beliefs.GetBehavior(observer.Id);
        Check(behavior.PrioritizeBell && behavior.MaintainBell && behavior.AttendDoctrineGathering,
            "A strong bell-rain believer should expose all three authored behavior biases.");
        var director = new VillageDirector();
        VillageTaskAssignment work = director.GetAssignment(observer, 400 * 60, true, behavior);
        VillageTaskAssignment gathering = director.GetAssignment(observer, 520 * 60, true, behavior);
        Check(work.Kind == VillageTaskKind.RingBell, "Belief should change second-work behavior to ringing.");
        Check(gathering.Kind == VillageTaskKind.DoctrineGather,
            "Belief should make the gathering explicitly doctrinal.");
    }

    private static void BeliefDeterministicAndStable()
    {
        ulong first = SimulateBeliefScript();
        ulong second = SimulateBeliefScript();
        Check(first == second, "A fixed logical command/event script must reproduce belief state exactly.");

        var world = NewWorld();
        var beliefs = new BeliefSimulation(MingzhongVillage.Roster);
        beliefs.Update(world);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
        {
            world.AdvanceTick();
            beliefs.Update(world);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Stable belief ticks should allocate 0 B, allocated {allocated} B.");
    }

    private static ulong SimulateBeliefScript()
    {
        var world = NewWorld();
        var beliefs = new BeliefSimulation(MingzhongVillage.Roster);
        for (int tick = 0; tick < 9 * 60 * MingzhongVillage.TicksPerSecond; tick++)
        {
            if (world.Tick == 38 * MingzhongVillage.TicksPerSecond)
                world.TryApply(MingzhongCommand.Rain(world.Tick, MingzhongVillage.Bell));
            if (world.Tick == 7L * 60 * MingzhongVillage.TicksPerSecond + 2 * MingzhongVillage.TicksPerSecond)
                world.TryApply(MingzhongCommand.Rain(world.Tick, new GridCell(35, 23)));
            world.AdvanceTick();
            beliefs.Update(world);
        }
        return world.ComputeStateHash() ^ RotateLeft(beliefs.ComputeStateHash(), 17);
    }

    private static (MingzhongWorldSimulation World, BeliefSimulation Beliefs, VillagerDefinition Observer)
        NewSingleObserverBeliefWorld(string villagerId)
    {
        VillagerDefinition observer = MingzhongVillage.Roster.Single(v => v.Id.Value == villagerId);
        observer = observer with { Home = MingzhongVillage.Bell, Work = MingzhongVillage.Bell };
        return (new MingzhongWorldSimulation(new[] { observer }),
            new BeliefSimulation(new[] { observer }), observer);
    }

    private static BeliefHypothesisKey BellRainKey() =>
        new(ObservationKind.BellRang, ObservationKind.RainStarted);

    private static ulong RotateLeft(ulong value, int amount) =>
        value << amount | value >> (64 - amount);

    private static void FamiliarSituationAndAffordances()
    {
        FamiliarSituation situation = FamiliarSituationClassifier.Classify(new FamiliarPerception(
            HasReachableFire: true,
            HasVillagerInDanger: true,
            HasBlockedWaterGate: true,
            HasDryCrop: true,
            IsHoldingWater: true,
            CanLocateWater: true,
            AreVillagersGathered: true));
        Check(situation == FamiliarSituation.FireEmergency,
            "Classifier must choose the highest authored priority instead of feature combinations.");
        Check(ApeFamiliarBody.Contains(
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.BlockedWaterGate),
                FamiliarAction.CarryObject),
            "Ape body must be able to carry a gate obstacle.");
        Check(!ApeFamiliarBody.Contains(
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.BlockedWaterGate),
                FamiliarAction.PourWater),
            "Situation/body filtering must reject irrelevant actions before Q scoring.");
    }

    private static void FamiliarDemonstrationAndReward()
    {
        var learning = new FamiliarLearning(42);
        learning.Demonstrate(FamiliarSituation.BlockedWaterGate, FamiliarAction.CarryObject, 0);
        Check(learning.GetQ(FamiliarSituation.BlockedWaterGate, FamiliarAction.CarryObject) == 800,
            "One demonstration should add the frozen +800 prior.");
        Check(learning.TryChoose(
                FamiliarSituation.BlockedWaterGate,
                FamiliarActionMask.CarryObject,
                0,
                out FamiliarDecision decision) && decision.Action == FamiliarAction.CarryObject,
            "The demonstrated legal action should be selected.");
        Check(learning.Reward(
                FamiliarRewardReason.GateOpened,
                FamiliarSituation.IdleVillage,
                FamiliarActionMask.Flee,
                1),
            "Gate result inside the credit window should update the last action.");
        Check(learning.GetQ(FamiliarSituation.BlockedWaterGate, FamiliarAction.CarryObject) == 1150,
            "Frozen integer Q formula should update 800 toward reward 1800 by exactly 350.");
    }

    private static void FamiliarHardFilters()
    {
        var learning = new FamiliarLearning(7);
        for (int i = 0; i < 10; i++)
            learning.Demonstrate(FamiliarSituation.BlockedWaterGate, FamiliarAction.PourWater, i);
        Check(learning.GetQ(FamiliarSituation.BlockedWaterGate, FamiliarAction.PourWater) == 8000,
            "Fixture should saturate an illegal action at maximum Q.");
        Check(learning.TryChoose(
                FamiliarSituation.BlockedWaterGate,
                FamiliarActionMask.CarryObject,
                20,
                out FamiliarDecision decision) && decision.Action == FamiliarAction.CarryObject,
            "An illegal action must never enter candidates even with maximum Q.");
        learning.Reward(
            FamiliarRewardReason.AffordanceFailed,
            FamiliarSituation.BlockedWaterGate,
            FamiliarActionMask.CarryObject | FamiliarActionMask.Flee,
            21);
        Check(learning.TryChoose(
                FamiliarSituation.BlockedWaterGate,
                FamiliarActionMask.CarryObject | FamiliarActionMask.Flee,
                80,
                out decision) && decision.Action == FamiliarAction.Flee,
            "A failed action must be removed during its 180-tick cooldown.");
    }

    private static void FamiliarPraiseStopAndCorrection()
    {
        var learning = new FamiliarLearning(99);
        learning.SetTrustPermille(1250);
        learning.Demonstrate(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater, 0);
        learning.TryChoose(
            FamiliarSituation.DryCropHoldingWater,
            FamiliarActionMask.PourWater,
            0,
            out _);
        int beforePraise = learning.GetQ(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater);
        Check(learning.Reward(
            FamiliarRewardReason.PlayerPraise,
            FamiliarSituation.IdleVillage,
            FamiliarActionMask.Flee,
            1), "Praise should reach the recent action.");
        int afterPraise = learning.GetQ(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater);
        Check(afterPraise > beforePraise, "High-trust praise should strengthen the contextual action.");

        Check(learning.Reward(
            FamiliarRewardReason.PlayerStop,
            FamiliarSituation.IdleVillage,
            FamiliarActionMask.Flee,
            2), "Stop should reach the same still-creditable action.");
        int afterStop = learning.GetQ(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater);
        Check(afterStop < afterPraise,
            "Stopping the same generalized 'dry plant' action should correct it gradually, not erase memory.");
        Check(learning.TraceCount >= 4 &&
              learning.GetTrace(learning.TraceCount - 1).Reason == FamiliarRewardReason.PlayerStop,
            "Dream/debug trace must explain the correction using the actual reward reason.");
    }

    private static void FamiliarSnapshotRestoresNextDecision()
    {
        var original = new FamiliarLearning(1234);
        original.Demonstrate(FamiliarSituation.BellGathering, FamiliarAction.RingBell, 0);
        original.TryChoose(
            FamiliarSituation.BellGathering,
            ApeFamiliarBody.GetLegalActions(FamiliarSituation.BellGathering),
            0,
            out _);
        FamiliarLearningSnapshot snapshot = original.CaptureSnapshot();
        var restored = new FamiliarLearning(9999);
        restored.RestoreSnapshot(snapshot);
        Check(original.ComputeStateHash() == restored.ComputeStateHash(),
            "Snapshot restore should reproduce Q, cooldown, trace and random state.");
        bool first = original.TryChoose(
            FamiliarSituation.IdleVillage,
            ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
            60,
            out FamiliarDecision expected);
        bool second = restored.TryChoose(
            FamiliarSituation.IdleVillage,
            ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
            60,
            out FamiliarDecision actual);
        Check(first == second && expected == actual,
            "The next controlled/exploratory choice must match after restore.");
    }

    private static void FamiliarTrainingDeterministicAndStable()
    {
        ulong first = SimulateFamiliarTraining();
        ulong second = SimulateFamiliarTraining();
        Check(first == second, "Fixed training and random seed must reproduce the same Q table and trace.");

        var learning = new FamiliarLearning(77);
        for (int tick = 0; tick < 64; tick++)
        {
            learning.TryChoose(
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                tick * 60L,
                out _);
            learning.Reward(
                FamiliarRewardReason.NoEffect,
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                tick * 60L + 1);
        }
        Check(learning.TraceCount == FamiliarLearning.TraceCapacity,
            "Decision explanation history must remain a 16-entry ring.");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 64; tick < 320; tick++)
        {
            learning.TryChoose(
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                tick * 60L,
                out _);
            learning.Reward(
                FamiliarRewardReason.NoEffect,
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                tick * 60L + 1);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Stable familiar decisions should allocate 0 B, allocated {allocated} B.");
    }

    private static ulong SimulateFamiliarTraining()
    {
        var learning = new FamiliarLearning(0xCAFE);
        for (int episode = 0; episode < 48; episode++)
        {
            FamiliarSituation situation = (FamiliarSituation)(episode % 7);
            FamiliarActionMask legal = ApeFamiliarBody.GetLegalActions(situation);
            long tick = episode * 240L;
            if (episode % 6 == 0)
                learning.Demonstrate(situation, FirstLegal(legal), tick);
            if (!learning.TryChoose(situation, legal, tick, out _)) continue;
            FamiliarRewardReason reward = (episode % 5) switch
            {
                0 => FamiliarRewardReason.PlayerPraise,
                1 => FamiliarRewardReason.NoEffect,
                2 => FamiliarRewardReason.AffordanceFailed,
                3 => FamiliarRewardReason.SafeDiscovery,
                _ => FamiliarRewardReason.PlayerStop
            };
            learning.Reward(reward, FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage), tick + 1);
        }
        return learning.ComputeStateHash();
    }

    private static FamiliarAction FirstLegal(FamiliarActionMask mask)
    {
        for (int i = 0; i < 6; i++)
        {
            FamiliarAction action = (FamiliarAction)i;
            if (ApeFamiliarBody.Contains(mask, action)) return action;
        }
        throw new InvalidOperationException("Expected at least one legal familiar action.");
    }

    private static void IslandNoInputRouteCompletes()
    {
        ScenarioResult result = SimulateIsland(ScenarioScript.NoInput);
        Check(result.Scenario.IsComplete, "The island must complete after exactly thirty minutes.");
        Check(result.Scenario.GateResolution == GateResolution.Villagers,
            "Villagers must clear an unresolved gate through the authored recovery route.");
        Check(result.Scenario.Ruin == RuinPuzzleState.Dry,
            "No rain on the tablet should leave the optional old route undiscovered.");
        Check(result.Scenario.Funeral == FuneralOutcome.LanternsPreserved,
            "Waiting through the funeral should preserve the paper lanterns.");
        Check(result.Scenario.Ending is IslandEnding.Endured or IslandEnding.Scarred,
            "Non-intervention must produce a stated outcome, never a technical game over.");
        Check(result.Scenario.Mural is { } mural && mural.Cost.Contains("灯", StringComparison.Ordinal),
            "The final triptych must state the cost of the chosen route.");
    }

    private static void IslandChoicesProduceDistinctMural()
    {
        ScenarioResult careful = SimulateIsland(ScenarioScript.RecoverAndReveal);
        ScenarioResult funeralRain = SimulateIsland(ScenarioScript.RainOnFuneral);
        Check(careful.Scenario.Ruin == RuinPuzzleState.Decoded,
            "Rain on the ruin should reveal and later decode the old canal grooves.");
        Check(careful.Scenario.Ending == IslandEnding.Flourished,
            "Recovered fields, an open gate and decoded ruin should support the flourishing ending.");
        Check(careful.Scenario.Funeral == FuneralOutcome.LanternsPreserved,
            "A restrained player should preserve the funeral lanterns.");
        Check(funeralRain.Scenario.Funeral == FuneralOutcome.LanternsLostToRain,
            "Rain covering the cemetery during the funeral must extinguish the lanterns.");
        Check(funeralRain.World.Observations.ToArray().Any(o => o.Kind == ObservationKind.FireExtinguished),
            "The funeral cost must be a visible causal event, not only an ending label.");
        Check(careful.Scenario.Mural != funeralRain.Scenario.Mural,
            "Different real histories must select different finite mural facts.");
    }

    private static void IslandChapterDeterministicAndStable()
    {
        ScenarioResult first = SimulateIsland(ScenarioScript.RecoverAndReveal);
        ScenarioResult second = SimulateIsland(ScenarioScript.RecoverAndReveal);
        Check(first.Hash == second.Hash && first.Scenario.Mural == second.Scenario.Mural,
            "The complete thirty-minute chapter must reproduce its state and triptych.");
        ulong beforeHash = first.Scenario.ComputeStateHash();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
            first.Scenario.Advance(first.World, first.Beliefs, first.Familiar);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && first.Scenario.ComputeStateHash() == beforeHash,
            $"Completed chapter should be a 0 B stable terminal state, allocated {allocated} B.");
    }

    private static ScenarioResult SimulateIsland(ScenarioScript script)
    {
        var world = NewWorld();
        var beliefs = new BeliefSimulation(MingzhongVillage.Roster);
        var familiar = new FamiliarLearning(0xBEEFUL);
        var scenario = new MingzhongIslandScenario();
        for (int i = 0; i < MingzhongIslandScenario.ChapterDurationTicks; i++)
        {
            if (script != ScenarioScript.NoInput &&
                world.Tick == 43L * MingzhongVillage.TicksPerSecond)
                world.TryApply(MingzhongCommand.Rain(world.Tick, new GridCell(41, 24), 5));
            if (script != ScenarioScript.NoInput &&
                world.Tick == 12L * 60 * MingzhongVillage.TicksPerSecond)
                world.TryApply(MingzhongCommand.Rain(world.Tick, MingzhongIslandScenario.RuinTablet, 4));
            if (script == ScenarioScript.RainOnFuneral &&
                world.Tick == 18L * 60 * MingzhongVillage.TicksPerSecond + 1)
                world.TryApply(MingzhongCommand.Rain(world.Tick, MingzhongIslandScenario.FuneralGround, 4));
            world.AdvanceTick();
            beliefs.Update(world);
            scenario.Advance(world, beliefs, familiar);
        }
        scenario.Advance(world, beliefs, familiar);
        ulong hash = world.ComputeStateHash() ^
                     RotateLeft(beliefs.ComputeStateHash(), 11) ^
                     RotateLeft(familiar.ComputeStateHash(), 23) ^
                     RotateLeft(scenario.ComputeStateHash(), 37);
        return new ScenarioResult(world, beliefs, familiar, scenario, hash);
    }

    private enum ScenarioScript
    {
        NoInput,
        RecoverAndReveal,
        RainOnFuneral
    }

    private readonly record struct ScenarioResult(
        MingzhongWorldSimulation World,
        BeliefSimulation Beliefs,
        FamiliarLearning Familiar,
        MingzhongIslandScenario Scenario,
        ulong Hash);

    private static void GameplayCommandRecordingValidatesProtocol()
    {
        ExpectThrows<ArgumentException>(() => new MingzhongCommandRecording(
            5,
            new[] { MingzhongCommand.Rain(6, new GridCell(1, 1)) }));
        ExpectThrows<ArgumentException>(() => new MingzhongCommandRecording(
            10,
            new[]
            {
                MingzhongCommand.Rain(5, new GridCell(1, 1)),
                MingzhongCommand.OpenGate(4)
            }));
        ExpectThrows<ArgumentException>(() => new MingzhongCommandRecording(
            10,
            new[] { MingzhongCommand.Rain(5, new GridCell(1, 1), 0) }));
        var valid = new MingzhongCommandRecording(
            20,
            new[]
            {
                MingzhongCommand.Rain(2, new GridCell(29, 4)),
                MingzhongCommand.OpenGate(2)
            });
        Check(valid.SchemaVersion == 1 && valid.Count == 2 && valid.EndTick == 20,
            "Command recording should retain its explicit protocol and terminal tick.");
    }

    private static void GameplayCommandJournalReproducesWorld()
    {
        var recordedWorld = NewWorld();
        MingzhongCommandJournal recorder = MingzhongCommandJournal.Record();
        for (int i = 0; i < 400; i++)
        {
            if (recordedWorld.Tick == 2)
            {
                MingzhongCommand rain = MingzhongCommand.Rain(2, new GridCell(29, 4));
                Check(recordedWorld.TryApply(rain), "Fixture rain should be accepted.");
                recorder.RecordAccepted(rain);
                MingzhongCommand gate = MingzhongCommand.OpenGate(2);
                Check(recordedWorld.TryApply(gate), "Fixture gate command should be accepted.");
                recorder.RecordAccepted(gate);
            }
            recordedWorld.AdvanceTick();
        }

        MingzhongCommandRecording recording = recorder.Snapshot(recordedWorld.Tick);
        var replayedWorld = NewWorld();
        var replayedFamiliar = new FamiliarLearning(0xC0FFEEUL);
        MingzhongCommandJournal playback = MingzhongCommandJournal.Play(recording);
        while (replayedWorld.Tick < recording.EndTick)
        {
            playback.ApplyCurrentTick(replayedWorld, replayedFamiliar);
            replayedWorld.AdvanceTick();
        }
        Check(playback.IsPlaybackComplete && playback.PlaybackCursor == 2,
            "Playback should consume both same-tick commands exactly once.");
        Check(recordedWorld.ComputeStateHash() == replayedWorld.ComputeStateHash(),
            "Final gameplay commands should reproduce the observable world without mouse pixels.");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) playback.ApplyCurrentTick(replayedWorld, replayedFamiliar);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, $"Completed command playback should allocate 0 B, allocated {allocated} B.");
    }

    private static void GameplayCommandPlaybackRejectsDivergence()
    {
        var missedWorld = NewWorld();
        var missed = MingzhongCommandJournal.Play(new MingzhongCommandRecording(
            10,
            new[] { MingzhongCommand.OpenGate(2) }));
        missedWorld.AdvanceTick();
        missedWorld.AdvanceTick();
        missedWorld.AdvanceTick();
        var missedFamiliar = new FamiliarLearning(1);
        ExpectThrows<InvalidOperationException>(() => missed.ApplyCurrentTick(missedWorld, missedFamiliar));

        var divergentWorld = NewWorld();
        divergentWorld.TryApply(MingzhongCommand.OpenGate(0));
        var divergent = MingzhongCommandJournal.Play(new MingzhongCommandRecording(
            1,
            new[] { MingzhongCommand.OpenGate(0) }));
        var divergentFamiliar = new FamiliarLearning(2);
        ExpectThrows<InvalidOperationException>(() => divergent.ApplyCurrentTick(divergentWorld, divergentFamiliar));
    }

    private static void GameplayCommandJournalReproducesFamiliarFeedback()
    {
        var recordedFamiliar = new FamiliarLearning(0x515151UL);
        var replayedFamiliar = new FamiliarLearning(0x515151UL);
        recordedFamiliar.Demonstrate(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater, 0);
        replayedFamiliar.Demonstrate(FamiliarSituation.DryCropHoldingWater, FamiliarAction.PourWater, 0);
        Check(recordedFamiliar.TryChoose(
                  FamiliarSituation.DryCropHoldingWater,
                  FamiliarActionMask.PourWater,
                  0,
                  out _), "Recorded familiar should choose the demonstrated action.");
        Check(replayedFamiliar.TryChoose(
                  FamiliarSituation.DryCropHoldingWater,
                  FamiliarActionMask.PourWater,
                  0,
                  out _), "Replayed familiar should begin from the same decision state.");

        MingzhongCommand feedback = MingzhongCommand.PraiseFamiliar(1);
        Check(recordedFamiliar.Reward(
                  FamiliarRewardReason.PlayerPraise,
                  FamiliarSituation.IdleVillage,
                  ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                  feedback.Tick), "Fixture praise should be accepted.");
        var recorder = MingzhongCommandJournal.Record();
        recorder.RecordAccepted(feedback);

        var replayedWorld = NewWorld();
        replayedWorld.AdvanceTick();
        MingzhongCommandJournal playback = MingzhongCommandJournal.Play(recorder.Snapshot(1));
        Check(playback.ApplyCurrentTick(replayedWorld, replayedFamiliar) == 1,
            "Playback should apply the recorded familiar feedback exactly once.");
        Check(recordedFamiliar.ComputeStateHash() == replayedFamiliar.ComputeStateHash(),
            "Familiar praise should reproduce the learned Q state and explanation trace.");
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
