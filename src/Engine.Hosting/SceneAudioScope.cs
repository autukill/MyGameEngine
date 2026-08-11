namespace GameEngine.Hosting;

using GameEngine.Features.Audio;

/// <summary>
/// Tracks Voices owned by the active Scene. Hosting stops the scope before releasing that Scene's
/// Content Package; use <see cref="Default2DGameContext.Audio"/> for deliberately global playback.
/// </summary>
public sealed class SceneAudioScope
{
    private readonly AudioRuntime? _audio;
    private readonly List<AudioVoiceRef> _voices = [];

    internal SceneAudioScope(AudioRuntime? audio) => _audio = audio;

    public bool Enabled => _audio is not null;

    public int TrackedVoiceCount => _voices.Count;

    public AudioVoiceRef Play(AudioClipRef clip, in AudioPlayOptions options)
    {
        AudioVoiceRef voice = RequireAudio().Play(clip, in options);
        _voices.Add(voice);
        return voice;
    }

    public bool TryPlay(
        AudioClipRef clip,
        in AudioPlayOptions options,
        out AudioVoiceRef voice)
    {
        if (!RequireAudio().TryPlay(clip, in options, out voice)) return false;
        _voices.Add(voice);
        return true;
    }

    public AudioVoiceRef PlayMusic(
        AudioClipRef clip,
        bool loop = true,
        float volume = 1f,
        int priority = 100)
    {
        var options = new AudioPlayOptions(
            AudioBusRef.Music,
            Volume: volume,
            Loop: loop,
            Priority: priority);
        return Play(clip, in options);
    }

    public bool IsPlaying(AudioVoiceRef voice) => RequireAudio().IsPlaying(voice);

    public bool Stop(AudioVoiceRef voice)
    {
        AudioRuntime audio = RequireAudio();
        bool stopped = audio.Stop(voice);
        RemoveTracked(voice);
        return stopped;
    }

    public void StopAll()
    {
        if (_audio is null)
        {
            _voices.Clear();
            return;
        }
        for (int i = _voices.Count - 1; i >= 0; i--)
            _audio.Stop(_voices[i]);
        _voices.Clear();
    }

    internal void PruneCompleted()
    {
        if (_audio is null)
        {
            _voices.Clear();
            return;
        }
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (!_audio.IsPlaying(_voices[i])) _voices.RemoveAt(i);
        }
    }

    private void RemoveTracked(AudioVoiceRef voice)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (_voices[i] == voice)
            {
                _voices.RemoveAt(i);
                return;
            }
        }
    }

    private AudioRuntime RequireAudio() => _audio ?? throw new InvalidOperationException(
        "Audio is not enabled. Call GameApplicationBuilder.UseAudio before Build.");
}
