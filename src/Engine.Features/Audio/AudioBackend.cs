namespace GameEngine.Features.Audio;

public readonly record struct AudioBackendVoice(long Value)
{
    public bool IsEmpty => Value == 0;
}

public readonly record struct AudioVoiceMix(
    float Volume,
    float Pan,
    float Pitch,
    bool Loop);

public interface IAudioBackend : IDisposable
{
    AudioBackendVoice Play(in AudioClipDescriptor clip, in AudioVoiceMix mix);

    void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix);

    bool IsPlaying(AudioBackendVoice voice);

    void Stop(AudioBackendVoice voice);
}

public readonly record struct AudioVoiceRef(int Slot, uint Generation)
{
    public static AudioVoiceRef Empty => default;

    public bool IsEmpty => Generation == 0;
}

public readonly record struct AudioPlayOptions(
    AudioBusRef Bus,
    float Volume = 1f,
    float Pan = 0f,
    float Pitch = 1f,
    bool Loop = false,
    int Priority = 0)
{
    public static AudioPlayOptions Sfx => new(AudioBusRef.Sfx);

    public static AudioPlayOptions Music => new(AudioBusRef.Music, Loop: true);
}

public readonly record struct AudioVoiceSnapshot(
    AudioVoiceRef Voice,
    AudioClipRef Clip,
    AudioBusRef Bus,
    float Volume,
    float Pan,
    float Pitch,
    bool Loop,
    int Priority,
    long StartSequence);

public readonly record struct AudioRuntimeDiagnostics(
    int ActiveVoices,
    int Capacity,
    long PlayRequests,
    long StartedVoices,
    long RejectedVoices,
    long StolenVoices,
    long BackendStops);
