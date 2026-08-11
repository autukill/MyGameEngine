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

public enum AudioSampleFormat
{
    Unsigned8,
    Signed16
}

/// <summary>Immutable interleaved PCM data owned by the logical AudioLibrary.</summary>
public sealed class DecodedAudioClip
{
    public DecodedAudioClip(
        byte[] pcmData,
        AudioSampleFormat format,
        int channels,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(pcmData);
        if (channels is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(channels), "Static PCM clips support mono or stereo data.");
        if (sampleRate is < 8_000 or > 384_000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        int bytesPerSample = format == AudioSampleFormat.Unsigned8 ? 1 : 2;
        int blockAlign = checked(channels * bytesPerSample);
        if (pcmData.Length == 0 || pcmData.Length % blockAlign != 0)
            throw new ArgumentException("PCM data must contain complete interleaved sample frames.", nameof(pcmData));

        PcmData = pcmData;
        Format = format;
        Channels = channels;
        SampleRate = sampleRate;
        FrameCount = pcmData.Length / blockAlign;
        Duration = TimeSpan.FromSeconds((double)FrameCount / sampleRate);
    }

    public ReadOnlyMemory<byte> PcmData { get; }
    public AudioSampleFormat Format { get; }
    public int Channels { get; }
    public int SampleRate { get; }
    public int FrameCount { get; }
    public TimeSpan Duration { get; }
}

public readonly record struct AudioClipDescriptor(
    AudioClipRef Clip,
    AudioSourceRef Source,
    AudioClipMetadata Metadata,
    DecodedAudioClip? Decoded = null,
    IAudioStreamFactory? StreamFactory = null)
{
    public AudioClipStorageKind StorageKind =>
        Decoded is not null ? AudioClipStorageKind.StaticPcm :
        StreamFactory is not null ? AudioClipStorageKind.Streaming :
        AudioClipStorageKind.MetadataOnly;
}

public sealed class AudioLibrary
{
    private readonly Dictionary<AudioClipRef, AudioClipDescriptor> _clips = [];
    internal event Action<AudioClipDescriptor>? ClipRemoved;

    public int Count => _clips.Count;

    public AudioClipRef Register(string name, string source, in AudioClipMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateMetadata(metadata);
        if (metadata.Streaming)
            throw new ArgumentException(
                "Streaming clips must be registered with an audio stream factory.", nameof(metadata));

        var clip = new AudioClipRef(name);
        var descriptor = new AudioClipDescriptor(clip, new AudioSourceRef(source), metadata);
        if (!_clips.TryAdd(clip, descriptor))
            throw new ArgumentException($"Audio clip '{name}' is already registered.", nameof(name));

        return clip;
    }

    public AudioClipRef RegisterStreaming(
        string name,
        string source,
        in AudioClipMetadata metadata,
        IAudioStreamFactory streamFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(streamFactory);
        ValidateMetadata(metadata);
        if (!metadata.Streaming)
            throw new ArgumentException(
                "Streaming registration requires AudioClipMetadata.Streaming to be true.", nameof(metadata));

        var clip = new AudioClipRef(name);
        var descriptor = new AudioClipDescriptor(
            clip,
            new AudioSourceRef(source),
            metadata,
            Decoded: null,
            streamFactory);
        if (!_clips.TryAdd(clip, descriptor))
            throw new ArgumentException($"Audio clip '{name}' is already registered.", nameof(name));
        return clip;
    }

    public AudioClipRef RegisterDecoded(string name, string source, DecodedAudioClip decoded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(decoded);

        var clip = new AudioClipRef(name);
        var metadata = new AudioClipMetadata(
            decoded.Duration,
            decoded.Channels,
            decoded.SampleRate,
            Streaming: false);
        var descriptor = new AudioClipDescriptor(
            clip,
            new AudioSourceRef(source),
            metadata,
            decoded);
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

    public bool Remove(AudioClipRef clip)
    {
        if (clip.IsEmpty || !_clips.Remove(clip, out AudioClipDescriptor descriptor))
            return false;
        ClipRemoved?.Invoke(descriptor);
        return true;
    }

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
