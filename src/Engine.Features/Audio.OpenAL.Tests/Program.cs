namespace Audio.OpenAL.Tests;

using GameEngine.Features.Audio;
using GameEngine.Features.Audio.OpenAL;

internal static class Program
{
    private static int Main()
    {
        var library = new AudioLibrary();
        var pcm = new byte[4_800 * sizeof(short)];
        AudioClipRef clip = library.RegisterDecoded(
            "backend.silence",
            "memory://backend-silence.wav",
            new DecodedAudioClip(pcm, AudioSampleFormat.Signed16, 1, 48_000));
        IAudioBackend backend = OpenAlAudioBackend.CreateOrSilent(out _, library);
        using (backend)
        {
            if (backend is not OpenAlAudioBackend and not SilentAudioBackend)
                return 1;
            AudioVoiceMix mix = new(0f, 0f, 1f, false);
            AudioClipDescriptor descriptor = library.Get(clip);
            AudioBackendVoice voice = backend.Play(in descriptor, in mix);
            if (voice.IsEmpty || !backend.IsPlaying(voice)) return 2;
            backend.Stop(voice);
            if (backend.IsPlaying(voice)) return 3;
            if (!library.Remove(clip)) return 4;
        }
        Console.WriteLine("=== Audio OpenAL playback/lifetime or silent fallback smoke passed ===");
        return 0;
    }
}
