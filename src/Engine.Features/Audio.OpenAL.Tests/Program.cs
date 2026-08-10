namespace Audio.OpenAL.Tests;

using System.Text;
using GameEngine.Features.Animation;
using GameEngine.Features.Audio;
using GameEngine.Features.Audio.OpenAL;
using GameEngine.Features.ContentAssets.Infrastructure;
using GameEngine.Features.Sprites.Infrastructure;
using GameEngine.Features.TextureAssets.Domain;
using GameEngine.Features.TextureAssets.Infrastructure;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        VerifyRegisteredPcmPlayback();
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

    private static void VerifyDeclarativeWavPackagePlayback()
    {
        Console.WriteLine("2. Declarative assets.json WAV package -> backend playback");
        string root = Directory.CreateTempSubdirectory("mygame-openal-assets-").FullName;
        try
        {
            const string clipName = "test.declarative.hit";
            const int sampleRate = 48_000;
            const int frameCount = 4_800;
            string wavPath = Path.Combine(root, "hit.wav");
            string manifestPath = Path.Combine(root, "assets.json");

            File.WriteAllBytes(
                wavPath,
                CreatePcm16Wave(
                    channels: 1,
                    sampleRate,
                    frameCount,
                    frequency: 440d));
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
                      }
                    ]
                  }
                  """,
                Encoding.UTF8);

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

            using IAudioBackend backend = OpenAlAudioBackend.CreateOrSilent(out _, audio);
            AudioVoiceMix mix = new(0f, 0f, 1f, Loop: true);
            AudioBackendVoice voice = backend.Play(in descriptor, in mix);
            Check(!voice.IsEmpty && backend.IsPlaying(voice),
                "Declarative WAV Clip starts one OpenAL/fallback Voice");

            package.Dispose();
            Check(audio.Count == 0,
                "Disposing the package removes its declarative Audio Clip");
            Check(backend.IsPlaying(voice),
                "Removing a Clip defers backend Buffer release until its active Voice stops");

            backend.Stop(voice);
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
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
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
}
