namespace Audio.Tests;

using GameEngine.Features.Audio;

internal static class Program
{
    private static int _failures;

    private static void Main()
    {
        Console.WriteLine("=== Audio Feature Smoke Test ===\n");
        VerifyLibrary();
        VerifyWaveDecoder();
        VerifyPlaybackAndBuses();
        VerifyVoiceStealing();
        VerifyLifecycle();
        VerifySilentBackend();
        VerifyStreamingContracts();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== All Audio smoke tests passed ==="
            : $"=== {_failures} Audio test(s) FAILED ===");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    private static void VerifyWaveDecoder()
    {
        Console.WriteLine("2. Real PCM WAV decoding");
        byte[] wav = CreatePcm16Wave(channels: 2, sampleRate: 48_000, frames: 480);
        using var stream = new MemoryStream(wav);
        DecodedAudioClip decoded = WaveAudioDecoder.Decode(stream);
        Check(decoded.Format == AudioSampleFormat.Signed16 &&
              decoded.Channels == 2 && decoded.SampleRate == 48_000 &&
              decoded.FrameCount == 480 && decoded.PcmData.Length == 1_920,
            "PCM16 WAV metadata and interleaved frames decode deterministically");

        var library = new AudioLibrary();
        AudioClipRef clip = library.RegisterDecoded("decoded", "memory://decoded.wav", decoded);
        Check(library.Get(clip).Decoded == decoded && library.Remove(clip) && !library.TryGet(clip, out _),
            "Decoded payload lifetime follows AudioLibrary registration/removal");
        CheckThrows<InvalidDataException>(
            () => WaveAudioDecoder.Decode(new MemoryStream("not-wave"u8.ToArray())),
            "Malformed WAV input is rejected");
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
        Console.WriteLine("3. Voice playback and bus mixing");
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
        Console.WriteLine("4. Deterministic voice limits and stealing");
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

        AudioRuntimeDiagnostics diagnostics = audio.CaptureDiagnostics();
        Check(diagnostics.PlayRequests == 4 && diagnostics.StartedVoices == 3 &&
              diagnostics.StolenVoices == 1 && diagnostics.RejectedVoices == 1,
            "Voice start, steal and rejection diagnostics are value snapshots");

        Check(audio.Stop(replacement) && !audio.Stop(replacement),
            "Stop is safe for stale generation handles");
    }

    private static void VerifyLifecycle()
    {
        Console.WriteLine("5. Runtime ownership and idempotent disposal");
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

    private static void VerifySilentBackend()
    {
        Console.WriteLine("6. Device-free silent fallback");
        var library = new AudioLibrary();
        AudioClipRef shortClip = library.Register(
            "short",
            "silent://short",
            new AudioClipMetadata(TimeSpan.FromTicks(1), 1, 48_000));
        using var backend = new SilentAudioBackend();
        using var runtime = new AudioRuntime(library, backend);
        AudioPlayOptions options = AudioPlayOptions.Sfx;
        AudioVoiceRef voice = runtime.Play(shortClip, in options);
        runtime.Update();
        Check(!runtime.IsPlaying(voice) && runtime.ActiveVoiceCount == 0,
            "Silent fallback completes one-shot voices without an audio device");
    }

    private static void VerifyStreamingContracts()
    {
        Console.WriteLine("7. Streaming clip contracts");
        var library = new AudioLibrary();
        var factory = new FakeStreamFactory(frameCount: 6, channels: 2, sampleRate: 48_000);
        var metadata = new AudioClipMetadata(
            TimeSpan.FromSeconds(6d / 48_000d),
            Channels: 2,
            SampleRate: 48_000,
            Streaming: true);
        AudioClipRef clip = library.RegisterStreaming(
            "music.stream",
            "memory://music.ogg",
            in metadata,
            factory);
        AudioClipDescriptor descriptor = library.Get(clip);
        Check(descriptor.StorageKind == AudioClipStorageKind.Streaming &&
              ReferenceEquals(descriptor.StreamFactory, factory) && descriptor.Decoded is null,
            "Streaming registration retains a factory without pre-decoded PCM");

        using (IAudioStreamSource source = descriptor.StreamFactory!.Open())
        {
            Span<byte> pcm = stackalloc byte[4 * sizeof(short) * 2];
            int first = source.ReadFrames(pcm);
            source.Seek(1);
            int second = source.ReadFrames(pcm[..(2 * source.BytesPerFrame)]);
            Check(first == 4 && second == 2 && source.PositionFrames == 3,
                "Per-voice source reads complete frames and supports exact seek");
        }
        Check(factory.OpenCount == 1 && factory.DisposeCount == 1,
            "Each opened streaming source has explicit ownership");

        CheckThrows<ArgumentException>(() => library.Register(
                "bad.stream",
                "bad.ogg",
                metadata),
            "Metadata-only registration cannot masquerade as a streaming clip");
        CheckThrows<ArgumentException>(() => library.RegisterStreaming(
                "bad.static",
                "bad.wav",
                metadata with { Streaming = false },
                factory),
            "Streaming factory requires streaming metadata");

        using var backend = new FakeAudioBackend();
        using var runtime = new AudioRuntime(library, backend);
        AudioPlayOptions options = AudioPlayOptions.Music;
        runtime.Play(clip, in options);
        runtime.Update();
        Check(backend.UpdateCount == 1, "AudioRuntime services backend queues before reclaiming voices");
    }

    private static byte[] CreatePcm16Wave(short channels, int sampleRate, int frames)
    {
        int blockAlign = channels * sizeof(short);
        int dataLength = frames * blockAlign;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * blockAlign);
            writer.Write((short)blockAlign);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);
        }
        return stream.ToArray();
    }

    private static AudioLibrary CreateLibrary(out AudioClipRef shot, out AudioClipRef music)
    {
        var library = new AudioLibrary();
        shot = library.Register("shot", "shot.ogg", new AudioClipMetadata(TimeSpan.FromMilliseconds(100), 1, 48_000));
        music = library.Register("music", "music.wav", new AudioClipMetadata(TimeSpan.FromMinutes(2), 2, 48_000));
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
        public int UpdateCount { get; private set; }

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

        public void Update() => UpdateCount++;

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeStreamFactory(int frameCount, int channels, int sampleRate) : IAudioStreamFactory
    {
        public int OpenCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IAudioStreamSource Open()
        {
            OpenCount++;
            return new FakeStreamSource(frameCount, channels, sampleRate, () => DisposeCount++);
        }
    }

    private sealed class FakeStreamSource(
        int frameCount,
        int channels,
        int sampleRate,
        Action disposed) : IAudioStreamSource
    {
        private bool _disposed;

        public AudioSampleFormat Format => AudioSampleFormat.Signed16;
        public int Channels { get; } = channels;
        public int SampleRate { get; } = sampleRate;
        public long FrameCount { get; } = frameCount;
        public long PositionFrames { get; private set; }
        public int BytesPerFrame => Channels * sizeof(short);

        public int ReadFrames(Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (destination.Length % BytesPerFrame != 0)
                throw new ArgumentException("Destination must contain complete PCM frames.", nameof(destination));
            int requested = destination.Length / BytesPerFrame;
            int read = (int)Math.Min(requested, FrameCount - PositionFrames);
            destination[..(read * BytesPerFrame)].Clear();
            PositionFrames += read;
            return read;
        }

        public void Seek(long frameOffset)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (frameOffset < 0 || frameOffset > FrameCount)
                throw new ArgumentOutOfRangeException(nameof(frameOffset));
            PositionFrames = frameOffset;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            disposed();
        }
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
