namespace GameEngine.Features.Audio;

using System.Diagnostics;

/// <summary>
/// Device-free backend used by headless runs and as an explicit fallback when audio initialization fails.
/// It preserves one-shot duration, looping and voice lifetime semantics without producing sound.
/// </summary>
public sealed class SilentAudioBackend : IAudioBackend
{
    private readonly Dictionary<AudioBackendVoice, SilentVoice> _voices = [];
    private long _nextVoice;
    private bool _disposed;

    public AudioBackendVoice Play(in AudioClipDescriptor clip, in AudioVoiceMix mix)
    {
        ThrowIfDisposed();
        var voice = new AudioBackendVoice(checked(++_nextVoice));
        _voices.Add(voice, new SilentVoice(Stopwatch.GetTimestamp(), clip.Metadata.Duration, mix.Pitch, mix.Loop));
        return voice;
    }

    public void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix)
    {
        ThrowIfDisposed();
        if (_voices.TryGetValue(voice, out SilentVoice current))
            _voices[voice] = current with { Pitch = mix.Pitch, Loop = mix.Loop };
    }

    public bool IsPlaying(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        if (!_voices.TryGetValue(voice, out SilentVoice current)) return false;
        if (current.Loop) return true;
        double elapsed = Stopwatch.GetElapsedTime(current.StartTimestamp).TotalSeconds;
        bool playing = elapsed < current.Duration.TotalSeconds / current.Pitch;
        if (!playing) _voices.Remove(voice);
        return playing;
    }

    public void Stop(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        _voices.Remove(voice);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _voices.Clear();
        _disposed = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct SilentVoice(
        long StartTimestamp,
        TimeSpan Duration,
        float Pitch,
        bool Loop);
}
