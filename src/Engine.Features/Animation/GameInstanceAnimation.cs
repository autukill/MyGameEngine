namespace GameEngine.Features.Animation;

using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.Gameplay;

/// <summary>Optional strongly typed sink for frame markers emitted by an attached animation.</summary>
public interface IAnimationEventHandler
{
    void OnAnimationEvent(in AnimationEvent animationEvent);
}

/// <summary>
/// Owner-local bridge from a shared AnimationLibrary to GameInstance Sprite/ImageIndex. It follows
/// the owner's active, pause and time-domain scheduling through the existing Behavior lifecycle.
/// </summary>
public sealed class SpriteAnimationBehavior : GameplayBehavior
{
    private readonly AnimationEventBuffer _events;
    private float _preservedImageSpeed = 1f;
    private bool _ownsImageIndex;

    public SpriteAnimationBehavior(AnimationLibrary library, int initialEventCapacity = 4)
    {
        Player = new AnimationPlayer(library ?? throw new ArgumentNullException(nameof(library)));
        _events = new AnimationEventBuffer(initialEventCapacity);
    }

    public AnimationPlayer Player { get; }
    public AnimationClipRef CurrentAnimation => Player.CurrentClip;
    public bool HasAnimation => !Player.CurrentClip.IsEmpty;

    public void Play(AnimationClipRef clip, bool restart = false, float speed = 1f)
    {
        if (!_ownsImageIndex)
        {
            _preservedImageSpeed = Owner.ImageSpeed;
            _ownsImageIndex = true;
        }

        Player.Play(clip, restart, speed);
        ApplyVisualState();
    }

    public void Pause() => Player.Pause();
    public void Resume() => Player.Resume();
    public void SetSpeed(float speed) => Player.SetSpeed(speed);

    public void Stop()
    {
        Player.Stop();
        ReleaseImageIndex();
    }

    public override void OnStep(double deltaTime)
    {
        if (!HasAnimation) return;
        Player.Update(deltaTime, _events);
        if (!HasAnimation)
        {
            ReleaseImageIndex();
            return;
        }
        ApplyVisualState();
        if (Owner is not IAnimationEventHandler handler) return;
        foreach (AnimationEvent item in _events.Items)
            handler.OnAnimationEvent(in item);
    }

    public override void OnDestroy() => ReleaseImageIndex();

    protected override void OnWriteGameplayState(ref GameplayStateWriter writer)
    {
        AnimationPlayerState state = Player.CaptureState();
        writer.Write("animation.clip", state.Clip.Name);
        writer.Write("animation.frame", state.ClipFrame);
        writer.Write("animation.direction", state.Direction);
        writer.Write("animation.cycleStart", state.CycleStartFrame);
        writer.Write("animation.accumulator", state.Accumulator);
        writer.Write("animation.cycles", state.CompletedCycles);
        writer.Write("animation.speed", state.Speed);
        writer.Write("animation.playing", state.IsPlaying);
        writer.Write("animation.complete", state.IsComplete);
    }

    private void ApplyVisualState()
    {
        Owner.Sprite = Player.CurrentSprite;
        Owner.ImageIndex = Player.CurrentSubImage;
        Owner.ImageSpeed = 0f;
    }

    private void ReleaseImageIndex()
    {
        if (!_ownsImageIndex) return;
        Owner.ImageSpeed = _preservedImageSpeed;
        _ownsImageIndex = false;
    }
}

public static class GameInstanceAnimationExtensions
{
    public static SpriteAnimationBehavior UseAnimations(
        this GameInstance instance,
        AnimationLibrary library,
        int initialEventCapacity = 4)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.UseBehavior(new SpriteAnimationBehavior(library, initialEventCapacity));
    }

    public static void PlayAnimation(
        this GameInstance instance,
        AnimationClipRef clip,
        bool restart = false,
        float speed = 1f) =>
        RequireAnimations(instance).Play(clip, restart, speed);

    public static void StopAnimation(this GameInstance instance) =>
        RequireAnimations(instance).Stop();

    public static SpriteAnimationBehavior RequireAnimations(this GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance.FindBehavior<SpriteAnimationBehavior>() ??
            throw new InvalidOperationException(
                "This GameInstance has no animation controller. Attach one with UseAnimations during construction.");
    }
}
