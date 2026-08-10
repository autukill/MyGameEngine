namespace AsteroidsPlayground;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Scene-local fact emitted once when an Asteroid's health is first depleted.
/// Multiple gameplay systems may react without the projectile knowing about them.
/// </summary>
public readonly record struct AsteroidDestroyedSignal(
    Vector2D Position,
    int Score);
