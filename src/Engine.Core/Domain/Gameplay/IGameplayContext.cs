namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Instance-scoped access to common gameplay operations. The owning Scene injects this context;
/// it is not a global service locator and exposes no rendering or GPU infrastructure.
/// </summary>
public interface IGameplayContext
{
    T Spawn<T>(T instance) where T : GameInstance;
    T Spawn<T>(PrefabRef<T> prefab, Vector2D position) where T : GameInstance;
    void Destroy(InstanceId id);
    GameInstance? FindById(InstanceId id);
    T? FindFirst<T>() where T : GameInstance;
    IReadOnlyList<T> FindAll<T>() where T : GameInstance;
    T? FirstCollision<T>(GameInstance source) where T : GameInstance;
    IReadOnlyList<T> Collisions<T>(GameInstance source) where T : GameInstance;
    IReadOnlyList<T> QueryArea<T>(Bounds2D bounds) where T : GameInstance;
    IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius) where T : GameInstance;
    void RequestScene(SceneRef scene);
}
