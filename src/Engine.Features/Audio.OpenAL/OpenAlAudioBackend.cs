namespace GameEngine.Features.Audio.OpenAL;

using Silk.NET.OpenAL;

/// <summary>OpenAL Soft backend for pre-decoded short PCM clips.</summary>
public sealed unsafe class OpenAlAudioBackend : IAudioBackend
{
    private readonly ALContext _alc;
    private readonly AL _al;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly AudioLibrary? _library;
    private readonly Dictionary<AudioBackendVoice, VoiceState> _voices = [];
    private readonly Dictionary<AudioClipRef, BufferState> _buffers = [];
    private long _nextVoice;
    private bool _disposed;

    public OpenAlAudioBackend(AudioLibrary? library = null, string? deviceName = null)
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();
        try
        {
            _device = _alc.OpenDevice(deviceName);
            if (_device is null)
                throw new InvalidOperationException("OpenAL could not open an audio output device.");
            _context = _alc.CreateContext(_device, null);
            if (_context is null)
                throw new InvalidOperationException("OpenAL could not create an audio context.");
            if (!_alc.MakeContextCurrent(_context))
                throw new InvalidOperationException("OpenAL could not activate the audio context.");
            _library = library;
            if (_library is not null) _library.ClipRemoved += OnClipRemoved;
        }
        catch
        {
            if (_context is not null) _alc.DestroyContext(_context);
            if (_device is not null) _alc.CloseDevice(_device);
            _al.Dispose();
            _alc.Dispose();
            throw;
        }
    }

    public static IAudioBackend CreateOrSilent(out string? failure, AudioLibrary? library = null)
    {
        try
        {
            failure = null;
            return new OpenAlAudioBackend(library);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or
                                   BadImageFormatException or EntryPointNotFoundException or
                                   TypeInitializationException or PlatformNotSupportedException or
                                   NotSupportedException)
        {
            failure = ex.Message;
            return new SilentAudioBackend();
        }
    }

    public AudioBackendVoice Play(in AudioClipDescriptor clip, in AudioVoiceMix mix)
    {
        ThrowIfDisposed();
        DecodedAudioClip decoded = clip.Decoded ?? throw new InvalidOperationException(
            $"Audio clip '{clip.Clip}' has no decoded PCM payload.");

        BufferState? buffer = null;
        uint source = 0;
        try
        {
            buffer = GetOrCreateBuffer(clip.Clip, decoded);
            buffer.ActiveVoices++;

            source = _al.GenSource();
            _al.SetSourceProperty(source, SourceInteger.Buffer, checked((int)buffer.Handle));
            ApplyMix(source, in mix);
            _al.SourcePlay(source);
            ThrowOnError("start playback");

            var voice = new AudioBackendVoice(checked(++_nextVoice));
            _voices.Add(voice, new VoiceState(source, buffer));
            return voice;
        }
        catch
        {
            if (source != 0) _al.DeleteSource(source);
            if (buffer is not null)
            {
                buffer.ActiveVoices--;
                TryDeleteReleasedBuffer(buffer);
            }
            throw;
        }
    }

    public void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix)
    {
        ThrowIfDisposed();
        if (_voices.TryGetValue(voice, out VoiceState state))
        {
            ApplyMix(state.Source, in mix);
            ThrowOnError("update voice mix");
        }
    }

    public bool IsPlaying(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        if (!_voices.TryGetValue(voice, out VoiceState state)) return false;
        _al.GetSourceProperty(state.Source, GetSourceInteger.SourceState, out int value);
        if ((SourceState)value == SourceState.Playing) return true;
        ReleaseVoice(voice, state);
        return false;
    }

    public void Stop(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        if (!_voices.TryGetValue(voice, out VoiceState state)) return;
        _al.SourceStop(state.Source);
        ReleaseVoice(voice, state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach ((AudioBackendVoice voice, VoiceState state) in _voices.ToArray())
        {
            _al.SourceStop(state.Source);
            ReleaseVoice(voice, state);
        }
        foreach (BufferState buffer in _buffers.Values)
            _al.DeleteBuffer(buffer.Handle);
        _buffers.Clear();
        if (_library is not null) _library.ClipRemoved -= OnClipRemoved;
        _alc.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al.Dispose();
        _alc.Dispose();
        _disposed = true;
    }

    private void ApplyMix(uint source, in AudioVoiceMix mix)
    {
        _al.SetSourceProperty(source, SourceFloat.Gain, mix.Volume);
        _al.SetSourceProperty(source, SourceFloat.Pitch, mix.Pitch);
        _al.SetSourceProperty(source, SourceBoolean.Looping, mix.Loop);
        _al.SetSourceProperty(source, SourceVector3.Position, mix.Pan, 0f, 0f);
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
    }

    private void ReleaseVoice(AudioBackendVoice voice, VoiceState state)
    {
        _voices.Remove(voice);
        _al.SetSourceProperty(state.Source, SourceInteger.Buffer, 0);
        _al.DeleteSource(state.Source);
        state.Buffer.ActiveVoices--;
        TryDeleteReleasedBuffer(state.Buffer);
    }

    private BufferState GetOrCreateBuffer(AudioClipRef clip, DecodedAudioClip decoded)
    {
        if (_buffers.TryGetValue(clip, out BufferState? existing))
        {
            if (ReferenceEquals(existing.Decoded, decoded)) return existing;
            existing.ReleaseRequested = true;
            _buffers.Remove(clip);
            TryDeleteReleasedBuffer(existing);
        }

        uint handle = _al.GenBuffer();
        try
        {
            BufferFormat format = ResolveFormat(decoded.Format, decoded.Channels);
            ReadOnlySpan<byte> data = decoded.PcmData.Span;
            fixed (byte* pointer = data)
                _al.BufferData(handle, format, pointer, data.Length, decoded.SampleRate);
            ThrowOnError("upload PCM data");
            var created = new BufferState(clip, decoded, handle);
            _buffers.Add(clip, created);
            return created;
        }
        catch
        {
            _al.DeleteBuffer(handle);
            throw;
        }
    }

    private void OnClipRemoved(AudioClipDescriptor descriptor)
    {
        if (!_buffers.Remove(descriptor.Clip, out BufferState? buffer)) return;
        buffer.ReleaseRequested = true;
        TryDeleteReleasedBuffer(buffer);
    }

    private void TryDeleteReleasedBuffer(BufferState buffer)
    {
        if (!buffer.ReleaseRequested || buffer.ActiveVoices != 0) return;
        _al.DeleteBuffer(buffer.Handle);
    }

    private void ThrowOnError(string operation)
    {
        AudioError error = _al.GetError();
        if (error != AudioError.NoError)
            throw new InvalidOperationException($"OpenAL failed to {operation}: {error}.");
    }

    private static BufferFormat ResolveFormat(AudioSampleFormat format, int channels) =>
        (format, channels) switch
        {
            (AudioSampleFormat.Unsigned8, 1) => BufferFormat.Mono8,
            (AudioSampleFormat.Unsigned8, 2) => BufferFormat.Stereo8,
            (AudioSampleFormat.Signed16, 1) => BufferFormat.Mono16,
            (AudioSampleFormat.Signed16, 2) => BufferFormat.Stereo16,
            _ => throw new NotSupportedException("OpenAL static clips support mono/stereo PCM8/PCM16.")
        };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct VoiceState(uint Source, BufferState Buffer);

    private sealed class BufferState(
        AudioClipRef clip,
        DecodedAudioClip decoded,
        uint handle)
    {
        public AudioClipRef Clip { get; } = clip;
        public DecodedAudioClip Decoded { get; } = decoded;
        public uint Handle { get; } = handle;
        public int ActiveVoices { get; set; }
        public bool ReleaseRequested { get; set; }
    }
}
