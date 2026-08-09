namespace AsteroidsPlayground;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.Input;

/// <summary>Unscaled input controller proving that Gameplay pause does not require a UI layer.</summary>
public sealed class PauseController : GameInstance
{
    private static readonly GameplayPauseKey PlayerPause = new("asteroids.player-pause");

    public PauseController() => TimeMode = InstanceTimeMode.Unscaled;

    public override void OnStep(double deltaTime)
    {
        if (KeyPressed(InputKey.P))
            ToggleGameplayPause(PlayerPause);
    }
}
