namespace GameEngine.Features.Animation;

public readonly record struct AnimationClipRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct AnimationEventRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

public enum AnimationLoopMode
{
    Once,
    Loop,
    PingPong
}

public readonly record struct AnimationFrameMarker(int ClipFrame, AnimationEventRef Event);

public sealed class AnimationClip
{
    private readonly int[] _subImages;
    private readonly AnimationFrameMarker[] _markers;

    internal AnimationClip(
        AnimationClipRef reference,
        ReadOnlySpan<int> subImages,
        float framesPerSecond,
        AnimationLoopMode loopMode,
        ReadOnlySpan<AnimationFrameMarker> markers)
    {
        Reference = reference;
        FramesPerSecond = framesPerSecond;
        LoopMode = loopMode;
        _subImages = subImages.ToArray();
        _markers = markers.ToArray();
    }

    public AnimationClipRef Reference { get; }

    public int FrameCount => _subImages.Length;

    public float FramesPerSecond { get; }

    public AnimationLoopMode LoopMode { get; }

    public ReadOnlySpan<int> SubImages => _subImages;

    public ReadOnlySpan<AnimationFrameMarker> Markers => _markers;

    public int GetSubImage(int clipFrame)
    {
        if ((uint)clipFrame >= (uint)_subImages.Length)
            throw new ArgumentOutOfRangeException(nameof(clipFrame));

        return _subImages[clipFrame];
    }
}

public sealed class AnimationLibrary
{
    private readonly Dictionary<AnimationClipRef, AnimationClip> _clips = [];

    public int Count => _clips.Count;

    public AnimationClipRef Register(
        string name,
        ReadOnlySpan<int> subImages,
        float framesPerSecond,
        AnimationLoopMode loopMode = AnimationLoopMode.Loop,
        ReadOnlySpan<AnimationFrameMarker> markers = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (subImages.IsEmpty)
            throw new ArgumentException("An animation clip requires at least one frame.", nameof(subImages));
        if (!float.IsFinite(framesPerSecond) || framesPerSecond <= 0f)
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond), "Animation FPS must be finite and positive.");
        if (!Enum.IsDefined(loopMode))
            throw new ArgumentOutOfRangeException(nameof(loopMode));

        for (var i = 0; i < subImages.Length; i++)
        {
            if (subImages[i] < 0)
                throw new ArgumentOutOfRangeException(nameof(subImages), "Sprite sub-images cannot be negative.");
        }

        // Keep author order stable when several events share one frame.
        var markerCopy = markers.ToArray();
        for (var i = 0; i < markerCopy.Length; i++)
        {
            AnimationFrameMarker marker = markerCopy[i];
            if ((uint)marker.ClipFrame >= (uint)subImages.Length)
                throw new ArgumentOutOfRangeException(nameof(markers), "Animation marker frame is outside the clip.");
            if (marker.Event.IsEmpty)
                throw new ArgumentException("Animation marker names cannot be empty.", nameof(markers));
        }

        var reference = new AnimationClipRef(name);
        var clip = new AnimationClip(reference, subImages, framesPerSecond, loopMode, markerCopy);
        if (!_clips.TryAdd(reference, clip))
            throw new ArgumentException($"Animation clip '{name}' is already registered.", nameof(name));

        return reference;
    }

    public AnimationClip Get(AnimationClipRef reference)
    {
        if (reference.IsEmpty)
            throw new ArgumentException("Animation clip reference cannot be empty.", nameof(reference));
        if (!_clips.TryGetValue(reference, out AnimationClip? clip))
            throw new KeyNotFoundException($"Animation clip '{reference}' is not registered.");

        return clip;
    }

    public bool TryGet(AnimationClipRef reference, out AnimationClip clip) =>
        _clips.TryGetValue(reference, out clip!);
}
