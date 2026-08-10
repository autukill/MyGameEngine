namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class AsteroidSpawner : GameInstance
{
    public static readonly PrefabRef<Asteroid, AsteroidSpawnArgs> AsteroidPrefab =
        new("asteroids.rock");
    private static readonly AlarmId SpawnTimer = new("spawn");

    private readonly GameplayRandom _random = new(0xA57E201DUL);
    private readonly InstanceRef<PlayerShip> _target;
    private readonly float _worldWidth;
    private readonly float _worldHeight;

    public AsteroidSpawner(
        InstanceRef<PlayerShip> target,
        float worldWidth,
        float worldHeight)
    {
        _target = target;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
    }

    public override void OnCreate() => SetAlarm(SpawnTimer, 0d);

    public override void OnAlarm(AlarmId alarm)
    {
        if (alarm != SpawnTimer) return;
        if (CountInstances<Asteroid>() < 24)
        {
            bool horizontalEdge = _random.Chance(0.5f);
            float x = horizontalEdge
                ? _random.Range(0f, _worldWidth)
                : (_random.Chance(0.5f) ? -30f : _worldWidth + 30f);
            float y = horizontalEdge
                ? (_random.Chance(0.5f) ? -30f : _worldHeight + 30f)
                : _random.Range(0f, _worldHeight);
            Vector2D targetPosition = Resolve(_target)?.Position ?? new Vector2D(
                _worldWidth * 0.5f,
                _worldHeight * 0.5f);
            Vector2D towardTarget = (targetPosition - new Vector2D(x, y)).Normalize();
            float speed = _random.Range(55f, 130f);
            float radius = _random.Range(16f, 34f);
            var spawn = new AsteroidSpawnArgs(
                new Vector2D(x, y),
                towardTarget * speed,
                radius,
                _worldWidth,
                _worldHeight);
            Spawn(AsteroidPrefab, spawn);
        }
        SetAlarm(SpawnTimer, 0.45d);
    }

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        writer.Write("spawner.random", _random.CaptureState());
        writer.Write("spawner.worldWidth", _worldWidth);
        writer.Write("spawner.worldHeight", _worldHeight);
    }
}
