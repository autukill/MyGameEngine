namespace FlappyBirdPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class PipeSpawner : GameInstance
{
    private const float PipeWidth = 76f;
    private const float PipeSpeed = 210f;
    private const float GapHeight = 154f;

    private static readonly SpawnSequence SpawnTimeline = new SpawnSequenceBuilder()
        .Wave(count: 1, intervalSeconds: 1.45d)
        .Build(SpawnSequenceRepeat.Loop, maximumConcurrent: 8);

    private readonly GameplayRandom _random = new(0xF1A99B17UL);
    private readonly SpawnSequencePlayer _spawns = new(SpawnTimeline);
    private readonly SpawnEmissionHandler _emit;
    private readonly InstanceRef<Bird> _bird;
    private readonly float _worldWidth;
    private readonly float _groundTop;

    public PipeSpawner(InstanceRef<Bird> bird, float worldWidth, float groundTop)
    {
        _bird = bird;
        _worldWidth = worldWidth;
        _groundTop = groundTop;
        _emit = EmitPipePair;
    }

    public override void OnStep(double deltaTime)
    {
        if (Resolve(_bird) is not { HasStarted: true }) return;
        _spawns.Update(deltaTime, CountInstances<ScoreGate>(), _emit);
    }

    private void EmitPipePair(in SpawnEmission emission)
    {
        float margin = GapHeight * 0.5f + 64f;
        float gapCenter = _random.Range(margin, _groundTop - margin);
        float topHeight = gapCenter - GapHeight * 0.5f;
        float bottomTop = gapCenter + GapHeight * 0.5f;
        float bottomHeight = _groundTop - bottomTop;
        float x = _worldWidth + PipeWidth;

        var top = new PipeSpawnArgs(
            new Vector2D(x, topHeight * 0.5f),
            PipeWidth,
            topHeight,
            PipeSpeed,
            IsTop: true);
        var bottom = new PipeSpawnArgs(
            new Vector2D(x, bottomTop + bottomHeight * 0.5f),
            PipeWidth,
            bottomHeight,
            PipeSpeed,
            IsTop: false);
        var gate = new ScoreGateSpawnArgs(
            new Vector2D(x, gapCenter),
            Width: 18f,
            Height: GapHeight - 12f,
            Speed: PipeSpeed);

        Spawn(GamePrefabs.Pipe, top);
        Spawn(GamePrefabs.Pipe, bottom);
        Spawn(GamePrefabs.ScoreGate, gate);
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("pipes.random", _random.CaptureState());
        SpawnSequencePlayerState state = _spawns.CaptureState();
        writer.Write("pipes.sequence.segment", state.SegmentIndex);
        writer.Write("pipes.sequence.iteration", state.Iteration);
        writer.Write("pipes.sequence.emissions", state.TotalEmissions);
        writer.Write("pipes.sequence.remaining", state.RemainingSeconds);
    }
}
