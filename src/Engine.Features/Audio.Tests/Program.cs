namespace Audio.Tests;

using GameEngine.Features.Audio;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Audio Feature Smoke Test ===\n");
        VerifyLibrary();
        VerifyPlaybackAndBuses();
        VerifyVoiceStealing();
        VerifyLifecycle();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Audio smoke tests passed ==="
            : $"=== {_failures} Audio test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyLibrary()
    {
        Console.WriteLine("1. Logical clip registration");
        var library = new AudioLibrary();
        AudioClipRef shot = library.Register(
            "player.shot",
            "audio/player-shot.ogg",
            new AudioClipMetadata(TimeSpan.FromMilliseconds(150), 1, 48_000));
        AudioClipDescriptor descriptor = library.Get(shot);
        Check(descriptor.Source == new AudioSourceRef("audio/player-shot.ogg") &&
              descriptor.Metadata.SampleRate == 48_000,
            "Clip metadata and logical source are retained without native handles");
        CheckThrows<ArgumentException>(() => library.Register(
                "player.shot", "other.wav", new AudioClipMetadata(TimeSpan.FromSeconds(1), 1, 44_100)),
            "Duplicate clip names are rejected");
        CheckThrows<ArgumentOutOfRangeException>(() => library.Register(
                "bad", "bad.wav", new AudioClipMetadata(TimeSpan.Zero, 1, 44_100)),
            "Invalid duration is rejected");
    }

    private static void VerifyPlaybackAndBuses()
    {
        Console.WriteLine("2. Voice playback and bus mixing");
        AudioLibrary library = CreateLibrary(out AudioClipRef shot, out _);
        using var backend = new FakeAudioBackend();
        using var audio = new AudioRuntime(library, backend, maxVoices: 4);

        AudioPlayOptions options = new(AudioBusRef.Sfx, Volume: 0.8f, Pan: -0.25f, Pitch: 1.5f);
        AudioVoiceRef voice = audio.Play(shot, in options);
        Check(audio.IsPlaying(voice) && audio.ActiveVoiceCount == 1, "Play returns a live generation-safe voice");
        Check(backend.LastMix.Volume == 0.8f && backend.LastMix.Pan == -0.25f && backend.LastMix.Pitch == 1.5f,
            "Backend receives validated voice mix");

        audio.SetBusVolume(AudioBusRef.Master, 0.5f);
        audio.SetBusVolume(AudioBusRef.Sfx, 0.25f);
        Check(Near(backend.LastMix.Volume, 0.1f), "Master, bus, and voice volume multiply deterministically");
        audio.SetBusMuted(AudioBusRef.Sfx, true);
        Check(backend.LastMix.Volume == 0f, "Muted bus updates active voices immediately");
        audio.SetBusMuted(AudioBusRef.Sfx, false);
        audio.SetVoiceVolume(voice, 1f);
        Check(Near(backend.LastMix.Volume, 0.125f), "Per-voice changes reapply the effective bus mix");

        backend.Complete(backend.LastVoice);
        audio.Update();
        Check(audio.ActiveVoiceCount == 0 && !audio.IsPlaying(voice), "Completed backend voices are reclaimed on Update");
    }

    private static void VerifyVoiceStealing()
    {
        Console.WriteLine("3. Deterministic voice limits and stealing");
        AudioLibrary library = CreateLibrary(out AudioClipRef shot, out AudioClipRef music);
        using var backend = new FakeAudioBackend();
        using var audio = new AudioRuntime(library, backend, maxVoices: 2);

        AudioPlayOptions low = new(AudioBusRef.Sfx, Priority: 1);
        AudioPlayOptions high = new(AudioBusRef.Music, Loop: true, Priority: 10);
        AudioVoiceRef oldestLow = audio.Play(shot, in low);
        AudioVoiceRef protectedMusic = audio.Play(music, in high);
        AudioVoiceRef replacement = audio.Play(shot, in low);
        Check(!audio.IsPlaying(oldestLow) && audio.IsPlaying(protectedMusic) && audio.IsPlaying(replacement),
            "Equal-priority requests steal the oldest eligible voice, not protected music");

        AudioPlayOptions tooLow = new(AudioBusRef.Sfx, Priority: 0);
        Check(!audio.TryPlay(shot, in tooLow, out AudioVoiceRef rejected) && rejected.IsEmpty,
            "A lower-priority request is rejected when every active voice is protected");

        Check(audio.Stop(replacement) && !audio.Stop(replacement),
            "Stop is safe for stale generation handles");
    }

    private static void VerifyLifecycle()
    {
        Console.WriteLine("4. Runtime ownership and idempotent disposal");
        AudioLibrary library = CreateLibrary(out AudioClipRef shot, out _);
        var backend = new FakeAudioBackend();
        var audio = new AudioRuntime(library, backend, ownsBackend: true);
        AudioPlayOptions options = AudioPlayOptions.Sfx;
        audio.Play(shot, in options);
        audio.Dispose();
        audio.Dispose();
        Check(backend.StopCount == 1 && backend.DisposeCount == 1,
            "Dispose stops live voices once and disposes an owned backend once");
        CheckThrows<ObjectDisposedException>(() => audio.Update(), "Disposed runtimes reject further use");
    }

    private static AudioLibrary CreateLibrary(out AudioClipRef shot, out AudioClipRef music)
    {
        var library = new AudioLibrary();
        shot = library.Register("shot", "shot.ogg", new AudioClipMetadata(TimeSpan.FromMilliseconds(100), 1, 48_000));
        music = library.Register("music", "music.ogg", new AudioClipMetadata(TimeSpan.FromMinutes(2), 2, 48_000, Streaming: true));
        return library;
    }

    private sealed class FakeAudioBackend : IAudioBackend
    {
        private readonly HashSet<AudioBackendVoice> _playing = [];
        private long _next;

        public AudioVoiceMix LastMix { get; private set; }
        public AudioBackendVoice LastVoice { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public AudioBackendVoice Play(in AudioClipDescriptor clip, in AudioVoiceMix mix)
        {
            LastVoice = new AudioBackendVoice(++_next);
            LastMix = mix;
            _playing.Add(LastVoice);
            return LastVoice;
        }

        public void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix)
        {
            if (!_playing.Contains(voice))
                throw new InvalidOperationException("Unknown fake voice.");
            LastMix = mix;
        }

        public bool IsPlaying(AudioBackendVoice voice) => _playing.Contains(voice);

        public void Stop(AudioBackendVoice voice)
        {
            if (_playing.Remove(voice))
                StopCount++;
        }

        public void Complete(AudioBackendVoice voice) => _playing.Remove(voice);

        public void Dispose() => DisposeCount++;
    }

    private static bool Near(float left, float right) => MathF.Abs(left - right) < 0.0001f;

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        _failures++;
        Console.WriteLine($"  [FAIL] {name}");
    }

    private static void CheckThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (TException)
        {
            Check(true, name);
        }
    }
}
