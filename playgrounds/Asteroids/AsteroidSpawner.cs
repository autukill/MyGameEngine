namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class AsteroidSpawner : GameInstance
{
    public static readonly PrefabRef<Asteroid, AsteroidSpawnArgs> AsteroidPrefab =
        new("asteroids.rock");
    private static readonly AlarmId SpawnTimer = new("spawn");

    private readonly Random _random = new(unchecked((int)0xA57E201D));
    private readonly float _worldWidth;
    private readonly float _worldHeight;

    public AsteroidSpawner(float worldWidth, float worldHeight)
    {
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
    }

    public override void OnCreate() => SetAlarm(SpawnTimer, 0d);

    public override void OnAlarm(AlarmId alarm)
    {
        if (alarm != SpawnTimer) return;
        if (FindAll<Asteroid>().Count < 24)
        {
            bool horizontalEdge = _random.Next(2) == 0;
            float x = horizontalEdge
                ? _random.NextSingle() * _worldWidth
                : (_random.Next(2) == 0 ? -30f : _worldWidth + 30f);
            float y = horizontalEdge
                ? (_random.Next(2) == 0 ? -30f : _worldHeight + 30f)
                : _random.NextSingle() * _worldHeight;
            Vector2D towardCenter = new Vector2D(
                _worldWidth * 0.5f - x,
                _worldHeight * 0.5f - y).Normalize();
            float speed = 55f + _random.NextSingle() * 75f;
            float radius = 16f + _random.NextSingle() * 18f;
            var spawn = new AsteroidSpawnArgs(
                new Vector2D(x, y),
                towardCenter * speed,
                radius,
                _worldWidth,
                _worldHeight);
            Spawn(AsteroidPrefab, spawn);
        }
        SetAlarm(SpawnTimer, 0.45d);
    }
}
