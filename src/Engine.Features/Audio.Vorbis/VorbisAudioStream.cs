namespace GameEngine.Features.Audio.Vorbis;

using System.Buffers.Binary;
using NVorbis;

/// <summary>Creates one fully managed OGG Vorbis decoder for each playing Voice.</summary>
public sealed class VorbisAudioStreamFactory : IAudioStreamFactory
{
    public VorbisAudioStreamFactory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }

    public IAudioStreamSource Open() => new VorbisAudioStreamSource(Path);

    public static AudioClipMetadata ReadMetadata(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new VorbisReader(System.IO.Path.GetFullPath(path));
        ValidateReader(reader, path);
        return new AudioClipMetadata(
            reader.TotalTime,
            reader.Channels,
            reader.SampleRate,
            Streaming: true);
    }

    internal static void ValidateReader(VorbisReader reader, string path)
    {
        if (reader.Channels is < 1 or > 2)
            throw new NotSupportedException(
                $"OGG Vorbis '{path}' has {reader.Channels} channels; streaming supports mono or stereo.");
        if (reader.SampleRate is < 8_000 or > 384_000)
            throw new InvalidDataException($"OGG Vorbis '{path}' has an invalid sample rate.");
        if (reader.TotalSamples <= 0 || reader.TotalTime <= TimeSpan.Zero)
            throw new InvalidDataException($"OGG Vorbis '{path}' contains no decodable audio frames.");
    }
}

internal sealed class VorbisAudioStreamSource : IAudioStreamSource
{
    private readonly VorbisReader _reader;
    private float[] _samples = [];
    private bool _disposed;

    public VorbisAudioStreamSource(string path)
    {
        _reader = new VorbisReader(path);
        try
        {
            VorbisAudioStreamFactory.ValidateReader(_reader, path);
            Channels = _reader.Channels;
            SampleRate = _reader.SampleRate;
            FrameCount = _reader.TotalSamples;
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public AudioSampleFormat Format => AudioSampleFormat.Signed16;
    public int Channels { get; }
    public int SampleRate { get; }
    public long FrameCount { get; }
    public long PositionFrames => _reader.SamplePosition;
    public int BytesPerFrame => checked(Channels * sizeof(short));

    public int ReadFrames(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Length % BytesPerFrame != 0)
            throw new ArgumentException("Destination must contain complete PCM frames.", nameof(destination));

        int requestedFrames = destination.Length / BytesPerFrame;
        if (requestedFrames == 0) return 0;
        int requestedSamples = checked(requestedFrames * Channels);
        if (_samples.Length < requestedSamples) _samples = new float[requestedSamples];

        int samplesRead = _reader.ReadSamples(_samples.AsSpan(0, requestedSamples));
        if (samplesRead % Channels != 0)
            throw new InvalidDataException("Vorbis decoder returned an incomplete interleaved PCM frame.");

        Span<byte> pcm = destination[..checked(samplesRead * sizeof(short))];
        for (var i = 0; i < samplesRead; i++)
        {
            float sample = Math.Clamp(_samples[i], -1f, 1f);
            short value = sample >= 0f
                ? (short)MathF.Round(sample * short.MaxValue)
                : (short)MathF.Round(sample * 32_768f);
            BinaryPrimitives.WriteInt16LittleEndian(pcm[(i * sizeof(short))..], value);
        }
        return samplesRead / Channels;
    }

    public void Seek(long frameOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frameOffset < 0 || frameOffset > FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frameOffset));
        _reader.SeekTo(frameOffset, SeekOrigin.Begin);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _reader.Dispose();
        _disposed = true;
    }
}
