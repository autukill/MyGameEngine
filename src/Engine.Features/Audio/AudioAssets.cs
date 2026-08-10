namespace GameEngine.Features.Audio;

public readonly record struct AudioClipRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct AudioSourceRef(string Name)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct AudioBusRef(string Name)
{
    public static AudioBusRef Master => new("master");
    public static AudioBusRef Music => new("music");
    public static AudioBusRef Sfx => new("sfx");

    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    public override string ToString() => Name ?? string.Empty;
}

public readonly record struct AudioClipMetadata(
    TimeSpan Duration,
    int Channels,
    int SampleRate,
    bool Streaming = false);

public readonly record struct AudioClipDescriptor(
    AudioClipRef Clip,
    AudioSourceRef Source,
    AudioClipMetadata Metadata);

public sealed class AudioLibrary
{
    private readonly Dictionary<AudioClipRef, AudioClipDescriptor> _clips = [];

    public int Count => _clips.Count;

    public AudioClipRef Register(string name, string source, in AudioClipMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateMetadata(metadata);

        var clip = new AudioClipRef(name);
        var descriptor = new AudioClipDescriptor(clip, new AudioSourceRef(source), metadata);
        if (!_clips.TryAdd(clip, descriptor))
            throw new ArgumentException($"Audio clip '{name}' is already registered.", nameof(name));

        return clip;
    }

    public AudioClipDescriptor Get(AudioClipRef clip)
    {
        if (clip.IsEmpty)
            throw new ArgumentException("Audio clip reference cannot be empty.", nameof(clip));
        if (!_clips.TryGetValue(clip, out AudioClipDescriptor descriptor))
            throw new KeyNotFoundException($"Audio clip '{clip}' is not registered.");

        return descriptor;
    }

    public bool TryGet(AudioClipRef clip, out AudioClipDescriptor descriptor) =>
        _clips.TryGetValue(clip, out descriptor);

    private static void ValidateMetadata(in AudioClipMetadata metadata)
    {
        if (metadata.Duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(metadata), "Audio duration must be positive.");
        if (metadata.Channels is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(metadata), "Audio channel count must be between one and eight.");
        if (metadata.SampleRate is < 8_000 or > 384_000)
            throw new ArgumentOutOfRangeException(nameof(metadata), "Audio sample rate is outside the supported range.");
    }
}
