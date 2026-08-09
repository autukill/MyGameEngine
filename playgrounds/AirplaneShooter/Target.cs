namespace AirplaneShooter;

using System.Numerics;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;
using GameEngine.Core.Domain.ValueObjects;

public sealed class Target : GameInstance, IHasGameplayHealth
{
    private const double SpawnDuration = .4d;
    private static readonly Vector2D SpawnScale = new(.35f, .35f);
    private static readonly Vector2D ActiveScale = new(6f, 2f);
    private static readonly Vector4 SpawnColor = new(1f, .2f, .25f, 0f);
    private static readonly Vector4 ActiveColor = new(1f, .2f, .25f, 1f);

    private readonly GameplayStateMachine<TargetState> _states;
    public GameplayHealth Health { get; } = new(3f);

    public Target(SpriteRef sprite, Vector2D position)
    {
        Sprite = sprite;
        Position = position;
        Scale = SpawnScale;
        Color = SpawnColor;
        _states = new GameplayStateMachine<TargetState>(TargetState.Spawning)
            .State(TargetState.Spawning, step: UpdateSpawning)
            .State(TargetState.Active, enter: Activate, step: UpdateActive);
    }

    public override void OnCreate() => _states.Start();

    public override void OnStep(double deltaTime) => _states.Update(deltaTime);

    private void UpdateSpawning(double deltaTime)
    {
        Scale = Tween.Lerp(
            SpawnScale,
            ActiveScale,
            _states.Elapsed,
            SpawnDuration,
            EasingKind.BackOut);
        Color = Tween.Lerp(
            SpawnColor,
            ActiveColor,
            _states.Elapsed,
            SpawnDuration,
            EasingKind.SineOut);
        RotateBy((float)deltaTime);

        if (_states.Elapsed >= SpawnDuration)
            _states.ChangeTo(TargetState.Active);
    }

    private void Activate()
    {
        Scale = ActiveScale;
        Color = ActiveColor;
        Collider = CollisionShape2D.Box(48f, 48f);
    }

    private void UpdateActive(double deltaTime) => RotateBy((float)deltaTime);

    private enum TargetState
    {
        Spawning,
        Active
    }
}
