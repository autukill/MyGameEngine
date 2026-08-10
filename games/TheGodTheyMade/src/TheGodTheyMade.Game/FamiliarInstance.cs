namespace TheGodTheyMade.Game;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;
using TheGodTheyMade.Game.Content;
using TheGodTheyMade.Simulation.Navigation;
using TheGodTheyMade.Simulation.Village;
using TheGodTheyMade.Simulation.World;
using TheGodTheyMade.Simulation.Familiar;

internal sealed class FamiliarInstance : GameInstance
{
    private readonly FamiliarLearning _learning;
    private readonly MingzhongWorldSimulation _world;
    private readonly NavigationGrid _navigation;
    private long _tick;
    private bool _demonstrated;
    private readonly bool _scripted;

    public FamiliarInstance(
        FamiliarLearning learning,
        MingzhongWorldSimulation world,
        NavigationGrid navigation,
        bool scripted)
        : base(
            "Familiar.Ape",
            new Vector2D(
                (MingzhongVillage.FamiliarRest.X + 0.5f) * MingzhongNavigation.TileSize,
                (MingzhongVillage.FamiliarRest.Y + 0.5f) * MingzhongNavigation.TileSize),
            new LayerDepth(5000 - MingzhongVillage.FamiliarRest.Y * MingzhongNavigation.TileSize))
    {
        _learning = learning;
        _world = world;
        _navigation = navigation;
        _scripted = scripted;
        Sprite = GameAssets.Sprites.DebugFamiliar;
        Color = new System.Numerics.Vector4(0.88f, 0.88f, 0.82f, 1f);
        Collider = CollisionShape2D.Circle(13f, new Vector2D(0f, -12f));
    }

    public override void OnStep(double deltaTime)
    {
        long demonstrationTick = _scripted ? 10 : 240L * MingzhongVillage.TicksPerSecond;
        long decisionTick = _scripted ? 20 : 260L * MingzhongVillage.TicksPerSecond;
        if (!_demonstrated && _tick >= demonstrationTick)
        {
            _learning.Demonstrate(
                FamiliarSituation.BlockedWaterGate,
                FamiliarAction.CarryObject,
                _tick);
            _demonstrated = true;
        }

        if (_tick >= decisionTick && _world.Gate == GateState.Blocked)
        {
            FamiliarSituation situation = FamiliarSituationClassifier.Classify(new FamiliarPerception(
                HasReachableFire: false,
                HasVillagerInDanger: false,
                HasBlockedWaterGate: true,
                HasDryCrop: false,
                IsHoldingWater: false,
                CanLocateWater: true,
                AreVillagersGathered: false));
            if (_learning.TryChoose(situation, ApeFamiliarBody.GetLegalActions(situation), _tick, out FamiliarDecision decision) &&
                decision.Action == FamiliarAction.CarryObject)
            {
                _world.Publish(ObservationKind.FamiliarActed, "familiar.ape", null, MingzhongVillage.Gate);
                if (_world.TryApply(MingzhongCommand.OpenGate(_world.Tick)))
                {
                    _navigation.SetBlocked(MingzhongNavigation.GateBoulder, false);
                    _learning.Reward(
                        FamiliarRewardReason.GateOpened,
                        FamiliarSituation.IdleVillage,
                        ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                        _tick);
                }
            }
        }

        if (ActionPressed(GameInputs.PraiseFamiliar))
            _learning.Reward(
                FamiliarRewardReason.PlayerPraise,
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                _tick);
        if (ActionPressed(GameInputs.StopFamiliar))
            _learning.Reward(
                FamiliarRewardReason.PlayerStop,
                FamiliarSituation.IdleVillage,
                ApeFamiliarBody.GetLegalActions(FamiliarSituation.IdleVillage),
                _tick);
        _tick++;
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("familiar.tick", _tick);
        writer.Write("familiar.learningHash", _learning.ComputeStateHash());
        writer.Write("familiar.traceCount", _learning.TraceCount);
        writer.Write("familiar.hasDecision", _learning.LastDecision is not null);
        if (_learning.LastDecision is { } decision)
        {
            writer.Write("familiar.situation", (int)decision.Situation);
            writer.Write("familiar.action", (int)decision.Action);
        }
    }
}
