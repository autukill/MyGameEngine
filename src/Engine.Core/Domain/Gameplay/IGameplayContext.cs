namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// Instance-scoped access to common gameplay operations. The owning Scene injects this context;
/// it is not a global service locator and exposes no rendering or GPU infrastructure.
/// </summary>
public interface IGameplayContext
{
    GameplayTimeController Time { get; }
    T Spawn<T>(T instance) where T : GameInstance;
    T Spawn<T>(PrefabRef<T> prefab, Vector2D position) where T : GameInstance;
    T Spawn<T, TArgs>(PrefabRef<T, TArgs> prefab, in TArgs args) where T : GameInstance;
    void Destroy(InstanceId id);
    GameInstance? FindById(InstanceId id);
    T? FindFirst<T>() where T : GameInstance;
    T? FindFirst<T>(GameplayTag tag) where T : GameInstance;
    IReadOnlyList<T> FindAll<T>() where T : GameInstance;
    IReadOnlyList<T> FindAll<T>(GameplayTag tag) where T : GameInstance;
    int FindAll<T>(GameplayQueryBuffer<T> results) where T : GameInstance;
    int FindAll<T>(GameplayTag tag, GameplayQueryBuffer<T> results) where T : GameInstance;
    int CountInstances<T>() where T : GameInstance;
    int CountInstances<T>(GameplayTag tag) where T : GameInstance;
    T? FirstCollision<T>(GameInstance source) where T : GameInstance;
    T? FirstCollision<T>(GameInstance source, GameplayTag tag) where T : GameInstance;
    IReadOnlyList<T> Collisions<T>(GameInstance source) where T : GameInstance;
    IReadOnlyList<T> Collisions<T>(GameInstance source, GameplayTag tag)
        where T : GameInstance;
    int Collisions<T>(GameInstance source, GameplayQueryBuffer<T> results)
        where T : GameInstance;
    int Collisions<T>(
        GameInstance source,
        GameplayTag tag,
        GameplayQueryBuffer<T> results) where T : GameInstance;
    IReadOnlyList<T> QueryArea<T>(Bounds2D bounds) where T : GameInstance;
    IReadOnlyList<T> QueryArea<T>(Bounds2D bounds, GameplayTag tag) where T : GameInstance;
    int QueryArea<T>(Bounds2D bounds, GameplayQueryBuffer<T> results)
        where T : GameInstance;
    int QueryArea<T>(
        Bounds2D bounds,
        GameplayTag tag,
        GameplayQueryBuffer<T> results) where T : GameInstance;
    IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius) where T : GameInstance;
    IReadOnlyList<T> QueryRadius<T>(Vector2D center, float radius, GameplayTag tag)
        where T : GameInstance;
    int QueryRadius<T>(Vector2D center, float radius, GameplayQueryBuffer<T> results)
        where T : GameInstance;
    int QueryRadius<T>(
        Vector2D center,
        float radius,
        GameplayTag tag,
        GameplayQueryBuffer<T> results) where T : GameInstance;
    void RequestScene(SceneRef scene);
    void RequestScene<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct;
    void PauseGameplay(GameInstance owner, GameplayPauseKey key);
    void ResumeGameplay(GameInstance owner, GameplayPauseKey key);
    void ToggleGameplayPause(GameInstance owner, GameplayPauseKey key);
    void ReleaseGameplayPauses(GameInstance owner);
}
