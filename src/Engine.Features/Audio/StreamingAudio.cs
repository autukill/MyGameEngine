namespace GameEngine.Features.Audio;

/// <summary>Describes how an audio clip supplies samples to a backend.</summary>
public enum AudioClipStorageKind
{
    MetadataOnly,
    StaticPcm,
    Streaming
}

/// <summary>
/// A per-voice, forward-readable PCM stream. Implementations own their decoder and source stream.
/// </summary>
public interface IAudioStreamSource : IDisposable
{
    AudioSampleFormat Format { get; }

    int Channels { get; }

    int SampleRate { get; }

    long FrameCount { get; }

    long PositionFrames { get; }

    int BytesPerFrame { get; }

    /// <summary>
    /// Decodes complete interleaved PCM frames into <paramref name="destination"/> and returns
    /// the number of frames written. Returning zero means end of stream.
    /// </summary>
    int ReadFrames(Span<byte> destination);

    /// <summary>Moves to an exact PCM frame. Used for deterministic restart and looping.</summary>
    void Seek(long frameOffset);
}

/// <summary>Creates an independent decoder/source for each concurrently playing voice.</summary>
public interface IAudioStreamFactory
{
    IAudioStreamSource Open();
}

internal static class AudioPcmLayout
{
    public static int BytesPerSample(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.Unsigned8 => 1,
        AudioSampleFormat.Signed16 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static int BytesPerFrame(AudioSampleFormat format, int channels) =>
        checked(BytesPerSample(format) * channels);
}
