namespace TheGodTheyMade.Game;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;

internal sealed class VillagerInstance : GameInstance
{
    private static readonly Vector4[] Palette =
    [
        new(0.88f, 0.55f, 0.24f, 1f), new(0.65f, 0.78f, 0.30f, 1f),
        new(0.30f, 0.72f, 0.85f, 1f), new(0.74f, 0.48f, 0.82f, 1f),
        new(0.88f, 0.36f, 0.34f, 1f), new(0.38f, 0.78f, 0.56f, 1f),
        new(0.82f, 0.74f, 0.40f, 1f), new(0.46f, 0.60f, 0.88f, 1f),
        new(0.88f, 0.52f, 0.66f, 1f), new(0.58f, 0.74f, 0.72f, 1f),
        new(0.78f, 0.60f, 0.38f, 1f), new(0.62f, 0.86f, 0.48f, 1f)
    ];

    private readonly VillagerDefinition _definition;
    private readonly NavigationGrid _navigation;
    private readonly NavigationQuery _query;
    private readonly VillageDirector _director;
    private readonly Func<bool> _gateBlocked;
    private readonly NavigationAgent _agent;
    private readonly MingzhongWorldSimulation _world;
    private VillageTaskAssignment _assignment;
    private int _plannedRevision = -1;
    private int _failedPlans;
    private int _retryTicks;
    private long _tick;

    public VillagerInstance(
        VillagerDefinition definition,
        int rosterIndex,
        NavigationGrid navigation,
        NavigationQuery query,
        VillageDirector director,
        Func<bool> gateBlocked,
        MingzhongWorldSimulation world)
        : base(
            $"Villager.{definition.Id.Value}",
            CellCenter(definition.Home),
            new LayerDepth(5000 - definition.Home.Y * MingzhongNavigation.TileSize))
    {
        _definition = definition;
        _navigation = navigation;
        _query = query;
        _director = director;
        _gateBlocked = gateBlocked;
        _world = world;
        _agent = new NavigationAgent(
            definition.Home,
            MingzhongNavigation.TileSize,
            74f,
            96);
        Sprite = GameAssets.Sprites.DebugVillager;
        Color = Palette[rosterIndex % Palette.Length];
        Collider = CollisionShape2D.Circle(7f, new Vector2D(0f, -7f));
        _assignment = new VillageTaskAssignment(
            VillageTaskKind.ReturnHome,
            definition.Home,
            -1);
        _world.SetVillagerCell(_definition.Id, definition.Home);
    }

    public override void OnStep(double deltaTime)
    {
        VillageTaskAssignment next = _director.GetAssignment(
            _definition, _tick, _gateBlocked());
        if (next != _assignment || _plannedRevision != _navigation.Revision)
        {
            _assignment = next;
            PlanPath();
        }
        else if (_retryTicks > 0)
        {
            _retryTicks--;
            if (_retryTicks == 0) PlanPath();
        }

        _agent.Update((float)deltaTime);
        Position = new Vector2D(_agent.Position.X, _agent.Position.Y);
        _world.SetVillagerCell(_definition.Id, _agent.CurrentCell);
        Depth = new LayerDepth(5000 - (int)Position.Y);
        _tick++;
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("villager.id", _definition.Id.Value);
        writer.Write("villager.tick", _tick);
        writer.Write("villager.task", (int)_assignment.Kind);
        writer.Write("villager.targetX", _assignment.Destination.X);
        writer.Write("villager.targetY", _assignment.Destination.Y);
        writer.Write("villager.pathIndex", _agent.PathIndex);
        writer.Write("villager.failedPlans", _failedPlans);
    }

    private void PlanPath()
    {
        GridCell target = _failedPlans >= 3 ? _definition.Home : _assignment.Destination;
        NavigationPathResult result = _agent.SetDestination(_query, _navigation, target);
        _plannedRevision = _navigation.Revision;
        if (result == NavigationPathResult.Success)
        {
            _failedPlans = 0;
            _retryTicks = 0;
            return;
        }

        _failedPlans++;
        _retryTicks = 30;
    }

    private static Vector2D CellCenter(GridCell cell) => new(
        (cell.X + 0.5f) * MingzhongNavigation.TileSize,
        (cell.Y + 0.5f) * MingzhongNavigation.TileSize);
}
