namespace Audio.OpenAL.Tests;

using System.Text;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.OpenAL;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;
using OggVorbisEncoder;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        VerifyRegisteredPcmPlayback();
        VerifyStreamingPcmQueuePlayback();
        VerifyDeclarativeWavPackagePlayback();

        Console.WriteLine(_failures == 0
            ? "=== Audio OpenAL tests passed ==="
            : $"=== Audio OpenAL tests failed: {_failures} ===");
        return _failures == 0 ? 0 : 1;
    }

    private static void VerifyRegisteredPcmPlayback()
    {
        Console.WriteLine("1. Registered PCM playback/lifetime or silent fallback");
        var library = new AudioLibrary();
        var pcm = new byte[4_800 * sizeof(short)];
        AudioClipRef clip = library.RegisterDecoded(
            "backend.silence",
            "memory://backend-silence.wav",
            new DecodedAudioClip(pcm, AudioSampleFormat.Signed16, 1, 48_000));

        using IAudioBackend backend = OpenAlAudioBackend.CreateOrSilent(out _, library);
        Check(backend is OpenAlAudioBackend or SilentAudioBackend,
            "OpenAL backend or deterministic silent fallback is selected");

        AudioVoiceMix mix = new(0f, 0f, 1f, Loop: true);
        AudioClipDescriptor descriptor = library.Get(clip);
        AudioBackendVoice voice = backend.Play(in descriptor, in mix);
        Check(!voice.IsEmpty && backend.IsPlaying(voice),
            "Registered decoded PCM starts one backend Voice");

        backend.Stop(voice);
        Check(!backend.IsPlaying(voice), "Stopped registered PCM Voice becomes inactive");
        Check(library.Remove(clip), "Registered PCM Clip can be removed");
    }

    private static void VerifyStreamingPcmQueuePlayback()
    {
        Console.WriteLine("2. Queued streaming PCM playback/lifetime or silent fallback");
        var library = new AudioLibrary();
        var factory = new GeneratedStreamFactory(frameCount: 48_000, channels: 2, sampleRate: 48_000);
        var metadata = new AudioClipMetadata(
            TimeSpan.FromSeconds(1),
            Channels: 2,
            SampleRate: 48_000,
            Streaming: true);
        AudioClipRef clip = library.RegisterStreaming(
            "backend.streaming",
            "generated://streaming",
            in metadata,
            factory);

        using IAudioBackend backend = OpenAlAudioBackend.CreateOrSilent(out _, library);
        AudioVoiceMix mix = new(0f, 0f, 1f, Loop: true);
        AudioClipDescriptor descriptor = library.Get(clip);
        AudioBackendVoice voice = backend.Play(in descriptor, in mix);
        backend.Update();
        Check(!voice.IsEmpty && backend.IsPlaying(voice),
            "Streaming Clip starts and remains live while queued data is serviced");
        backend.Stop(voice);
        Check(!backend.IsPlaying(voice), "Stopping a streaming Voice releases its playback instance");
        Check(backend is SilentAudioBackend || factory.OpenCount == 1 && factory.DisposeCount == 1,
            "OpenAL owns exactly one decoder per streaming Voice and disposes it on Stop");
        Check(library.Remove(clip), "Streaming Clip registration can be removed independently");
    }

    private static void VerifyDeclarativeWavPackagePlayback()
    {
        Console.WriteLine("3. Declarative assets.json WAV package -> backend playback");
        string root = Directory.CreateTempSubdirectory("mygame-openal-assets-").FullName;
        try
        {
            const string clipName = "test.declarative.hit";
            const int sampleRate = 48_000;
            const int frameCount = 4_800;
            string wavPath = Path.Combine(root, "hit.wav");
            string oggPath = Path.Combine(root, "music.ogg");
            string manifestPath = Path.Combine(root, "assets.json");

            File.WriteAllBytes(
                wavPath,
                CreatePcm16Wave(
                    channels: 1,
                    sampleRate,
                    frameCount,
                    frequency: 440d));
            File.WriteAllBytes(oggPath, CreateVorbisOgg());
            File.WriteAllText(
                manifestPath,
                $$"""
                  {
                    "schemaVersion": 1,
                    "id": "openal.declarative-tests",
                    "dependencies": [],
                    "audioClips": [
                      {
                        "name": "{{clipName}}",
                        "path": "hit.wav",
                        "streaming": false
                      },
                      {
                        "name": "test.declarative.music",
                        "path": "music.ogg",
                        "streaming": true
                      }
                    ]
                  }
                  """,
                System.Text.Encoding.UTF8);

            var textureBackend = new FakeTextureBackend();
            using var textures = new TextureLibrary(textureBackend);
            var sprites = new SpriteLibrary(textures);
            var animations = new AnimationLibrary();
            var audio = new AudioLibrary();
            using var packages = new ContentPackageManager(
                textures,
                sprites,
                animations,
                audio,
                root);
            using LoadedContentPackage package = packages.Load("assets.json");

            AudioClipRef clip = package.GetAudioClip(clipName);
            AudioClipDescriptor descriptor = audio.Get(clip);
            Check(
                descriptor.Decoded is
                {
                    Format: AudioSampleFormat.Signed16,
                    Channels: 1,
                    SampleRate: sampleRate,
                    FrameCount: frameCount
                },
                "assets.json resolves and decodes the referenced PCM16 Mono WAV");
            Check(
                descriptor.Source.Name == Path.GetFullPath(wavPath),
                "Declarative Clip keeps the resolved WAV source path");
            Check(
                descriptor.Metadata.Duration == TimeSpan.FromMilliseconds(100),
                "Decoded WAV metadata exposes its 100 ms duration");
            AudioClipDescriptor music = audio.Get(package.GetAudioClip("test.declarative.music"));
            Check(music.StorageKind == AudioClipStorageKind.Streaming && music.Decoded is null,
                "Declarative OGG remains compressed and exposes a streaming source");

            using IAudioBackend backend = OpenAlAudioBackend.CreateOrSilent(out _, audio);
            AudioVoiceMix mix = new(0f, 0f, 1f, Loop: true);
            AudioBackendVoice voice = backend.Play(in descriptor, in mix);
            Check(!voice.IsEmpty && backend.IsPlaying(voice),
                "Declarative WAV Clip starts one OpenAL/fallback Voice");
            AudioBackendVoice musicVoice = backend.Play(in music, in mix);
            backend.Update();
            Check(!musicVoice.IsEmpty && backend.IsPlaying(musicVoice),
                "Declarative OGG Clip crosses Content -> decoder -> queued backend playback");

            package.Dispose();
            Check(audio.Count == 0,
                "Disposing the package removes its declarative Audio Clip");
            Check(backend.IsPlaying(voice),
                "Removing a Clip defers backend Buffer release until its active Voice stops");

            backend.Stop(voice);
            backend.Stop(musicVoice);
            Check(!backend.IsPlaying(voice),
                "Stopping the last Voice releases the declarative playback instance");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreatePcm16Wave(
        short channels,
        int sampleRate,
        int frameCount,
        double frequency)
    {
        int blockAlign = channels * sizeof(short);
        int dataLength = checked(frameCount * blockAlign);
        using var stream = new MemoryStream(44 + dataLength);
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

            for (int frame = 0; frame < frameCount; frame++)
            {
                double progress = frame / (double)frameCount;
                double envelope = 1d - progress;
                short sample = (short)(
                    Math.Sin(2d * Math.PI * frequency * frame / sampleRate) *
                    envelope *
                    8_000d);
                for (int channel = 0; channel < channels; channel++)
                    writer.Write(sample);
            }
        }
        return stream.ToArray();
    }

    private static byte[] CreateVorbisOgg()
    {
        const int channels = 2;
        const int sampleRate = 44_100;
        const int frameCount = 4_410;
        var samples = new float[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            samples[channel] = new float[frameCount];
            for (var frame = 0; frame < frameCount; frame++)
                samples[channel][frame] = 0.1f * MathF.Sin(2f * MathF.PI * 330f * frame / sampleRate);
        }

        using var output = new MemoryStream();
        VorbisInfo info = VorbisInfo.InitVariableBitRate(channels, sampleRate, 0.3f);
        var ogg = new OggStream(0x4D4747);
        var comments = new Comments();
        comments.AddTag("ENCODER", "MyGameEngine.Audio.OpenAL.Tests");
        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushOggPages(ogg, output, force: true);

        ProcessingState state = ProcessingState.Create(info);
        for (var offset = 0; offset < frameCount; offset += 512)
        {
            int length = Math.Min(512, frameCount - offset);
            state.WriteData(samples, length, offset);
            while (!ogg.Finished && state.PacketOut(out OggPacket packet))
            {
                ogg.PacketIn(packet);
                FlushOggPages(ogg, output, force: false);
            }
        }
        state.WriteEndOfStream();
        while (!ogg.Finished && state.PacketOut(out OggPacket packet))
        {
            ogg.PacketIn(packet);
            FlushOggPages(ogg, output, force: false);
        }
        FlushOggPages(ogg, output, force: true);
        return output.ToArray();
    }

    private static void FlushOggPages(OggStream ogg, Stream output, bool force)
    {
        while (ogg.PageOut(out OggPage page, force))
        {
            output.Write(page.Header);
            output.Write(page.Body);
        }
    }

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

    private sealed class FakeTextureBackend : ITextureBackend
    {
        private uint _nextHandle = 1;

        public uint CreateTexture(
            int width,
            int height,
            ReadOnlySpan<byte> rgbaPixels,
            TextureSampler sampler) => _nextHandle++;

        public void DeleteTexture(uint handle)
        {
        }
    }

    private sealed class GeneratedStreamFactory(int frameCount, int channels, int sampleRate) : IAudioStreamFactory
    {
        public int OpenCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IAudioStreamSource Open()
        {
            OpenCount++;
            return new GeneratedStreamSource(frameCount, channels, sampleRate, () => DisposeCount++);
        }
    }

    private sealed class GeneratedStreamSource(
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
                throw new ArgumentException("Destination must contain complete frames.", nameof(destination));
            int read = (int)Math.Min(destination.Length / BytesPerFrame, FrameCount - PositionFrames);
            Span<short> samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(
                destination[..(read * BytesPerFrame)]);
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (short)((PositionFrames + i / Channels) % 128);
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
}
