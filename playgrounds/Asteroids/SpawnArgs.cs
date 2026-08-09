namespace AsteroidsPlayground;

using GameEngine.Core.Domain.ValueObjects;

public readonly record struct LaserSpawnArgs(
    Vector2D Position,
    Vector2D Velocity);

public readonly record struct AsteroidSpawnArgs(
    Vector2D Position,
    Vector2D Velocity,
    float Radius,
    float WorldWidth,
    float WorldHeight);
