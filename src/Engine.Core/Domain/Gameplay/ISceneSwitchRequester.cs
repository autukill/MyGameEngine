namespace GameEngine.Core.Domain.Gameplay;

/// <summary>
/// Narrow boundary implemented by Hosting to queue Scene switches after the current Step.
/// Generic arguments stay typed until Hosting stores the pending activation.
/// </summary>
public interface ISceneSwitchRequester
{
    void Request(SceneRef scene);

    void Request<TArgs>(SceneRef<TArgs> scene, in TArgs args) where TArgs : struct;
}
