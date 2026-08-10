namespace GameEngine.Features.Animation;

public readonly record struct AnimationEvent(
    AnimationClipRef Clip,
    AnimationEventRef Event,
    int ClipFrame,
    int SubImage,
    long CompletedCycles);

public sealed class AnimationEventBuffer
{
    private AnimationEvent[] _items;

    public AnimationEventBuffer(int initialCapacity = 4)
    {
        if (initialCapacity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));

        _items = initialCapacity == 0 ? [] : new AnimationEvent[initialCapacity];
    }

    public int Count { get; private set; }

    public ReadOnlySpan<AnimationEvent> Items => _items.AsSpan(0, Count);

    public void Clear() => Count = 0;

    internal void Add(in AnimationEvent item)
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, Math.Max(4, _items.Length * 2));

        _items[Count++] = item;
    }
}

public readonly record struct AnimationUpdateResult(
    int PreviousSubImage,
    int CurrentSubImage,
    int AdvancedFrames,
    int CompletedCycles,
    bool JustCompleted);

public readonly record struct AnimationPlayerState(
    AnimationClipRef Clip,
    int ClipFrame,
    int Direction,
    int CycleStartFrame,
    double Accumulator,
    long CompletedCycles,
    float Speed,
    bool IsPlaying,
    bool IsComplete);

public sealed class AnimationPlayer
{
    private readonly AnimationLibrary _library;
    private AnimationClip? _clip;
    private double _accumulator;
    private int _clipFrame;
    private int _direction = 1;
    private int _cycleStartFrame;

    public AnimationPlayer(AnimationLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public AnimationClipRef CurrentClip => _clip?.Reference ?? default;

    public GameEngine.Core.Domain.ValueObjects.SpriteRef CurrentSprite =>
        _clip?.Sprite ?? default;

    public bool IsPlaying { get; private set; }

    public bool IsComplete { get; private set; }

    public float Speed { get; private set; } = 1f;

    public int ClipFrame => _clipFrame;

    public int CurrentSubImage => _clip?.GetSubImage(_clipFrame) ?? 0;

    public long CompletedCycles { get; private set; }

    public AnimationPlayerState CaptureState() => new(
        CurrentClip,
        _clipFrame,
        _direction,
        _cycleStartFrame,
        _accumulator,
        CompletedCycles,
        Speed,
        IsPlaying,
        IsComplete);

    public void RestoreState(in AnimationPlayerState state)
    {
        if (state.Clip.IsEmpty)
        {
            AnimationPlayerState stopped = new(
                default,
                ClipFrame: 0,
                Direction: 1,
                CycleStartFrame: 0,
                Accumulator: 0d,
                CompletedCycles: 0,
                Speed: 1f,
                IsPlaying: false,
                IsComplete: false);
            if (state != default && state != stopped)
                throw new ArgumentException("An empty animation state must represent a stopped player.", nameof(state));
            Stop();
            return;
        }

        AnimationClip clip = _library.Get(state.Clip);
        if ((uint)state.ClipFrame >= (uint)clip.FrameCount ||
            (uint)state.CycleStartFrame >= (uint)clip.FrameCount ||
            state.Direction is not (-1 or 1) ||
            !double.IsFinite(state.Accumulator) || state.Accumulator < 0d ||
            state.CompletedCycles < 0 || !float.IsFinite(state.Speed) || state.Speed == 0f ||
            state.IsPlaying && state.IsComplete)
        {
            throw new ArgumentException("Animation player state is invalid.", nameof(state));
        }

        _clip = clip;
        _clipFrame = state.ClipFrame;
        _direction = state.Direction;
        _cycleStartFrame = state.CycleStartFrame;
        _accumulator = state.Accumulator;
        CompletedCycles = state.CompletedCycles;
        Speed = state.Speed;
        IsPlaying = state.IsPlaying;
        IsComplete = state.IsComplete;
    }

    public void Play(AnimationClipRef clip, bool restart = false, float speed = 1f)
    {
        ValidateSpeed(speed);
        AnimationClip next = _library.Get(clip);
        if (!restart && ReferenceEquals(_clip, next))
        {
            Speed = speed;
            if (!IsComplete)
                IsPlaying = true;
            return;
        }

        _clip = next;
        Speed = speed;
        _direction = speed > 0f ? 1 : -1;
        _clipFrame = _direction > 0 ? 0 : next.FrameCount - 1;
        _cycleStartFrame = _clipFrame;
        _accumulator = 0d;
        CompletedCycles = 0;
        IsComplete = false;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Resume()
    {
        if (_clip is not null && !IsComplete)
            IsPlaying = true;
    }

    public void Stop()
    {
        _clip = null;
        _clipFrame = 0;
        _accumulator = 0d;
        CompletedCycles = 0;
        IsPlaying = false;
        IsComplete = false;
        Speed = 1f;
        _direction = 1;
        _cycleStartFrame = 0;
    }

    public void SetSpeed(float speed)
    {
        ValidateSpeed(speed);
        if (Math.Sign(speed) != Math.Sign(Speed))
            _direction = -_direction;
        Speed = speed;
    }

    public AnimationUpdateResult Update(double deltaTime, AnimationEventBuffer? events = null)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Animation delta time must be finite and non-negative.");

        events?.Clear();
        RefreshClip();
        int previous = CurrentSubImage;
        if (!IsPlaying || _clip is null || deltaTime == 0d)
            return new AnimationUpdateResult(previous, previous, 0, 0, false);

        double frameDuration = 1d / (_clip.FramesPerSecond * Math.Abs(Speed));
        _accumulator += deltaTime;
        var advanced = 0;
        var completedThisUpdate = 0;
        var justCompleted = false;

        while (IsPlaying && _accumulator + 1e-12d >= frameDuration)
        {
            _accumulator -= frameDuration;
            if (_accumulator < 0d)
                _accumulator = 0d;

            if (AdvanceOneFrame())
            {
                completedThisUpdate++;
                CompletedCycles++;
            }

            advanced++;
            EmitMarkers(events);
            if (IsComplete)
                justCompleted = true;
        }

        return new AnimationUpdateResult(
            previous,
            CurrentSubImage,
            advanced,
            completedThisUpdate,
            justCompleted);
    }

    private void RefreshClip()
    {
        if (_clip is null) return;
        if (!_library.TryGet(_clip.Reference, out AnimationClip? live))
        {
            Stop();
            return;
        }
        if (ReferenceEquals(live, _clip)) return;
        _clip = live;
        _clipFrame = Math.Clamp(_clipFrame, 0, live.FrameCount - 1);
        _cycleStartFrame = _direction > 0 ? 0 : live.FrameCount - 1;
        _accumulator = 0d;
    }

    private bool AdvanceOneFrame()
    {
        AnimationClip clip = _clip!;
        if (clip.FrameCount == 1)
        {
            if (clip.LoopMode == AnimationLoopMode.Once)
            {
                IsPlaying = false;
                IsComplete = true;
            }
            return clip.LoopMode != AnimationLoopMode.Once;
        }

        int next = _clipFrame + _direction;
        if ((uint)next < (uint)clip.FrameCount)
        {
            _clipFrame = next;
            if (clip.LoopMode == AnimationLoopMode.Once &&
                (_clipFrame == 0 || _clipFrame == clip.FrameCount - 1))
            {
                IsPlaying = false;
                IsComplete = true;
                return true;
            }

            return clip.LoopMode == AnimationLoopMode.PingPong && _clipFrame == _cycleStartFrame;
        }

        switch (clip.LoopMode)
        {
            case AnimationLoopMode.Once:
                _clipFrame = _direction > 0 ? clip.FrameCount - 1 : 0;
                IsPlaying = false;
                IsComplete = true;
                return true;

            case AnimationLoopMode.Loop:
                _clipFrame = _direction > 0 ? 0 : clip.FrameCount - 1;
                return true;

            case AnimationLoopMode.PingPong:
                _direction = -_direction;
                _clipFrame += _direction;
                return _clipFrame == 0 || _clipFrame == clip.FrameCount - 1;

            default:
                throw new InvalidOperationException($"Unsupported animation loop mode '{clip.LoopMode}'.");
        }
    }

    private void EmitMarkers(AnimationEventBuffer? events)
    {
        if (events is null)
            return;

        AnimationClip clip = _clip!;
        foreach (AnimationFrameMarker marker in clip.Markers)
        {
            if (marker.ClipFrame != _clipFrame)
                continue;

            var item = new AnimationEvent(
                clip.Reference,
                marker.Event,
                _clipFrame,
                clip.GetSubImage(_clipFrame),
                CompletedCycles);
            events.Add(in item);
        }
    }

    private static void ValidateSpeed(float speed)
    {
        if (!float.IsFinite(speed) || speed == 0f)
            throw new ArgumentOutOfRangeException(nameof(speed), "Animation speed must be finite and non-zero.");
    }
}
