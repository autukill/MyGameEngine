namespace GameEngine.Features.Animation;

using GameEngine.Core.Domain.ValueObjects;

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

public sealed record AnimationReplacementSource(
    string Name,
    SpriteRef Sprite,
    int[] SubImages,
    float FramesPerSecond,
    AnimationLoopMode LoopMode,
    AnimationFrameMarker[] Markers);

public sealed class AnimationClip
{
    private readonly int[] _subImages;
    private readonly AnimationFrameMarker[] _markers;

    internal AnimationClip(
        AnimationClipRef reference,
        SpriteRef sprite,
        ReadOnlySpan<int> subImages,
        float framesPerSecond,
        AnimationLoopMode loopMode,
        ReadOnlySpan<AnimationFrameMarker> markers)
    {
        Reference = reference;
        Sprite = sprite;
        FramesPerSecond = framesPerSecond;
        LoopMode = loopMode;
        _subImages = subImages.ToArray();
        _markers = markers.ToArray();
    }

    public AnimationClipRef Reference { get; }

    public SpriteRef Sprite { get; }

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

    public AnimationReplacementTransaction BeginReplacement(
        IReadOnlyCollection<string> replaceableNames,
        IReadOnlyList<AnimationReplacementSource> replacements)
    {
        ArgumentNullException.ThrowIfNull(replaceableNames);
        ArgumentNullException.ThrowIfNull(replacements);
        var scope = new HashSet<string>(replaceableNames, StringComparer.Ordinal);
        if (scope.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Replaceable Animation names cannot be empty.", nameof(replaceableNames));

        var staged = new Dictionary<AnimationClipRef, AnimationClip>();
        foreach (AnimationReplacementSource replacement in replacements)
        {
            ArgumentNullException.ThrowIfNull(replacement);
            AnimationClip clip = BuildClip(
                replacement.Name,
                replacement.Sprite,
                replacement.SubImages,
                replacement.FramesPerSecond,
                replacement.LoopMode,
                replacement.Markers);
            if (_clips.ContainsKey(clip.Reference) && !scope.Contains(clip.Reference.Name))
                throw new InvalidOperationException(
                    $"Animation '{clip.Reference}' is owned outside the replacement scope.");
            if (!staged.TryAdd(clip.Reference, clip))
                throw new ArgumentException(
                    $"Replacement Animation '{clip.Reference}' appears more than once.",
                    nameof(replacements));
        }

        var previous = _clips
            .Where(pair => scope.Contains(pair.Key.Name))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return new AnimationReplacementTransaction(this, scope, staged, previous);
    }

    public AnimationClipRef Register(
        string name,
        SpriteRef sprite,
        ReadOnlySpan<int> subImages,
        float framesPerSecond,
        AnimationLoopMode loopMode = AnimationLoopMode.Loop,
        ReadOnlySpan<AnimationFrameMarker> markers = default)
    {
        AnimationClip clip = BuildClip(name, sprite, subImages, framesPerSecond, loopMode, markers);
        if (!_clips.TryAdd(clip.Reference, clip))
            throw new ArgumentException($"Animation clip '{name}' is already registered.", nameof(name));

        return clip.Reference;
    }

    private static AnimationClip BuildClip(
        string name,
        SpriteRef sprite,
        ReadOnlySpan<int> subImages,
        float framesPerSecond,
        AnimationLoopMode loopMode,
        ReadOnlySpan<AnimationFrameMarker> markers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sprite.IsEmpty)
            throw new ArgumentException("An animation clip requires a Sprite.", nameof(sprite));
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

        return new AnimationClip(
            new AnimationClipRef(name),
            sprite,
            subImages,
            framesPerSecond,
            loopMode,
            markerCopy);
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

    public bool Remove(AnimationClipRef reference) =>
        !reference.IsEmpty && _clips.Remove(reference);

    public sealed class AnimationReplacementTransaction : IDisposable
    {
        private AnimationLibrary? _owner;
        private readonly HashSet<string> _scope;
        private readonly Dictionary<AnimationClipRef, AnimationClip> _staged;
        private readonly Dictionary<AnimationClipRef, AnimationClip> _previous;
        private bool _active;
        private bool _committed;

        internal AnimationReplacementTransaction(
            AnimationLibrary owner,
            HashSet<string> scope,
            Dictionary<AnimationClipRef, AnimationClip> staged,
            Dictionary<AnimationClipRef, AnimationClip> previous)
        {
            _owner = owner;
            _scope = scope;
            _staged = staged;
            _previous = previous;
        }

        public void Activate()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            if (_active) throw new InvalidOperationException("Animation replacement is already active.");
            foreach (string name in _scope) _owner._clips.Remove(new AnimationClipRef(name));
            foreach (var pair in _staged) _owner._clips.Add(pair.Key, pair.Value);
            _active = true;
        }

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_owner is null, this);
            if (!_active) throw new InvalidOperationException("Activate the Animation replacement first.");
            _committed = true;
            _owner = null;
        }

        public void Dispose()
        {
            AnimationLibrary? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null || _committed || !_active) return;
            foreach (AnimationClipRef reference in _staged.Keys) owner._clips.Remove(reference);
            foreach (var pair in _previous) owner._clips.Add(pair.Key, pair.Value);
        }
    }
}
