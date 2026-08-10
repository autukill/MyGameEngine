namespace TheGodTheyMade.Game;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;
using GameEngine.Features.Audio;
using TheGodTheyMade.Simulation.World;

internal sealed class ScenarioAudioFeedback : GameInstance
{
    private readonly MingzhongWorldSimulation _world;
    private readonly AudioRuntime _audio;
    private readonly AudioClipRef _bell;
    private readonly AudioClipRef _rain;
    private readonly AudioClipRef _gate;
    private readonly AudioClipRef _funeral;
    private ulong _lastEventId;

    public ScenarioAudioFeedback(
        MingzhongWorldSimulation world,
        AudioRuntime audio,
        AudioClipRef bell,
        AudioClipRef rain,
        AudioClipRef gate,
        AudioClipRef funeral)
        : base("Mingzhong.AudioFeedback", Vector2D.Zero, LayerDepth.Instances)
    {
        _world = world;
        _audio = audio;
        _bell = bell;
        _rain = rain;
        _gate = gate;
        _funeral = funeral;
    }

    public override void OnStep(double deltaTime)
    {
        foreach (ref readonly WorldObservation observation in _world.Observations)
        {
            if (observation.Id.Value <= _lastEventId) continue;
            _lastEventId = observation.Id.Value;
            AudioClipRef clip = observation.Kind switch
            {
                ObservationKind.BellRang => _bell,
                ObservationKind.RainStarted => _rain,
                ObservationKind.GateOpened => _gate,
                ObservationKind.FuneralStarted or ObservationKind.FireExtinguished => _funeral,
                _ => default
            };
            if (!clip.IsEmpty)
                _audio.TryPlay(clip, AudioPlayOptions.Sfx, out _);
        }
    }
}
