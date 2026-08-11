namespace GameEngine.Features.Audio.OpenAL;

using Silk.NET.OpenAL;

/// <summary>OpenAL Soft backend for pre-decoded clips and queued PCM streams.</summary>
public sealed unsafe class OpenAlAudioBackend : IAudioBackend
{
    private const int StreamingBufferCount = 4;
    private const int StreamingFramesPerBuffer = 4_096;
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
        return clip.StorageKind switch
        {
            AudioClipStorageKind.StaticPcm => PlayStatic(in clip, in mix),
            AudioClipStorageKind.Streaming => PlayStreaming(in clip, in mix),
            _ => throw new InvalidOperationException(
                $"Audio clip '{clip.Clip}' has neither decoded PCM nor a streaming source.")
        };
    }

    private AudioBackendVoice PlayStatic(in AudioClipDescriptor clip, in AudioVoiceMix mix)
    {
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
            ApplyMix(source, in mix, useNativeLooping: true);
            _al.SourcePlay(source);
            ThrowOnError("start playback");

            var voice = new AudioBackendVoice(checked(++_nextVoice));
            _voices.Add(voice, VoiceState.ForStatic(source, buffer));
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

    private AudioBackendVoice PlayStreaming(in AudioClipDescriptor clip, in AudioVoiceMix mix)
    {
        IAudioStreamFactory factory = clip.StreamFactory ?? throw new InvalidOperationException(
            $"Audio clip '{clip.Clip}' has no streaming source factory.");
        IAudioStreamSource? decoder = null;
        StreamingVoiceState? streaming = null;
        uint source = 0;
        try
        {
            decoder = factory.Open() ?? throw new InvalidOperationException(
                $"Streaming factory for '{clip.Clip}' returned no source.");
            ValidateStream(clip, decoder);

            source = _al.GenSource();
            uint[] buffers = _al.GenBuffers(StreamingBufferCount);
            streaming = new StreamingVoiceState(
                decoder,
                buffers,
                new byte[checked(StreamingFramesPerBuffer * decoder.BytesPerFrame)],
                mix.Loop);
            decoder = null;

            foreach (uint buffer in buffers)
            {
                if (!FillStreamingBuffer(streaming, buffer)) break;
                uint queued = buffer;
                _al.SourceQueueBuffers(source, 1, &queued);
                streaming.QueuedBuffers++;
            }
            if (streaming.QueuedBuffers == 0)
                throw new InvalidDataException($"Streaming audio clip '{clip.Clip}' contains no PCM frames.");

            ApplyMix(source, in mix, useNativeLooping: false);
            _al.SourcePlay(source);
            ThrowOnError("start streaming playback");

            var voice = new AudioBackendVoice(checked(++_nextVoice));
            _voices.Add(voice, VoiceState.ForStreaming(source, streaming));
            return voice;
        }
        catch
        {
            if (source != 0) _al.DeleteSource(source);
            if (streaming is not null)
            {
                foreach (uint buffer in streaming.Buffers) _al.DeleteBuffer(buffer);
                streaming.Dispose();
            }
            decoder?.Dispose();
            throw;
        }
    }

    public void SetMix(AudioBackendVoice voice, in AudioVoiceMix mix)
    {
        ThrowIfDisposed();
        if (_voices.TryGetValue(voice, out VoiceState? state))
        {
            if (state.Streaming is not null) state.Streaming.Loop = mix.Loop;
            ApplyMix(state.Source, in mix, useNativeLooping: state.Streaming is null);
            ThrowOnError("update voice mix");
        }
    }

    public bool IsPlaying(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        if (!_voices.TryGetValue(voice, out VoiceState? state)) return false;
        if (state.Streaming is not null) ServiceStreaming(state);
        _al.GetSourceProperty(state.Source, GetSourceInteger.SourceState, out int value);
        if ((SourceState)value == SourceState.Playing) return true;
        if (state.Streaming is { QueuedBuffers: > 0 })
        {
            _al.SourcePlay(state.Source);
            ThrowOnError("restart an underrun streaming source");
            return true;
        }
        ReleaseVoice(voice, state);
        return false;
    }

    public void Stop(AudioBackendVoice voice)
    {
        ThrowIfDisposed();
        if (!_voices.TryGetValue(voice, out VoiceState? state)) return;
        _al.SourceStop(state.Source);
        ReleaseVoice(voice, state);
    }

    public void Update()
    {
        ThrowIfDisposed();
        foreach (VoiceState state in _voices.Values)
        {
            if (state.Streaming is not null) ServiceStreaming(state);
        }
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

    private void ApplyMix(uint source, in AudioVoiceMix mix, bool useNativeLooping)
    {
        _al.SetSourceProperty(source, SourceFloat.Gain, mix.Volume);
        _al.SetSourceProperty(source, SourceFloat.Pitch, mix.Pitch);
        _al.SetSourceProperty(source, SourceBoolean.Looping, useNativeLooping && mix.Loop);
        _al.SetSourceProperty(source, SourceVector3.Position, mix.Pan, 0f, 0f);
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
    }

    private void ReleaseVoice(AudioBackendVoice voice, VoiceState state)
    {
        _voices.Remove(voice);
        _al.DeleteSource(state.Source);
        if (state.StaticBuffer is not null)
        {
            state.StaticBuffer.ActiveVoices--;
            TryDeleteReleasedBuffer(state.StaticBuffer);
        }
        if (state.Streaming is not null)
        {
            foreach (uint buffer in state.Streaming.Buffers) _al.DeleteBuffer(buffer);
            state.Streaming.Dispose();
        }
    }

    private void ServiceStreaming(VoiceState voice)
    {
        StreamingVoiceState streaming = voice.Streaming!;
        _al.GetSourceProperty(voice.Source, GetSourceInteger.BuffersProcessed, out int processed);
        for (var i = 0; i < processed; i++)
        {
            uint buffer = 0;
            _al.SourceUnqueueBuffers(voice.Source, 1, &buffer);
            streaming.QueuedBuffers--;
            if (FillStreamingBuffer(streaming, buffer))
            {
                _al.SourceQueueBuffers(voice.Source, 1, &buffer);
                streaming.QueuedBuffers++;
            }
        }
        ThrowOnError("refill streaming buffers");

        if (streaming.QueuedBuffers <= 0) return;
        _al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out int value);
        if ((SourceState)value != SourceState.Playing)
        {
            _al.SourcePlay(voice.Source);
            ThrowOnError("recover a streaming underrun");
        }
    }

    private bool FillStreamingBuffer(StreamingVoiceState streaming, uint buffer)
    {
        int frames = streaming.Decoder.ReadFrames(streaming.DecodeBuffer);
        if (frames == 0 && streaming.Loop)
        {
            streaming.Decoder.Seek(0);
            frames = streaming.Decoder.ReadFrames(streaming.DecodeBuffer);
        }
        if (frames == 0) return false;

        int byteCount = checked(frames * streaming.Decoder.BytesPerFrame);
        BufferFormat format = ResolveFormat(streaming.Decoder.Format, streaming.Decoder.Channels);
        fixed (byte* pointer = streaming.DecodeBuffer)
            _al.BufferData(buffer, format, pointer, byteCount, streaming.Decoder.SampleRate);
        return true;
    }

    private static void ValidateStream(in AudioClipDescriptor clip, IAudioStreamSource stream)
    {
        if (stream.Channels is < 1 or > 2)
            throw new NotSupportedException("OpenAL streaming clips support mono or stereo PCM.");
        if (stream.SampleRate != clip.Metadata.SampleRate || stream.Channels != clip.Metadata.Channels)
            throw new InvalidDataException(
                $"Streaming source for '{clip.Clip}' does not match its registered metadata.");
        int expectedBytesPerFrame = AudioPcmLayout.BytesPerFrame(stream.Format, stream.Channels);
        if (stream.BytesPerFrame != expectedBytesPerFrame || stream.FrameCount <= 0)
            throw new InvalidDataException($"Streaming source for '{clip.Clip}' has invalid PCM layout metadata.");
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

    private sealed class VoiceState
    {
        private VoiceState(uint source, BufferState? staticBuffer, StreamingVoiceState? streaming)
        {
            Source = source;
            StaticBuffer = staticBuffer;
            Streaming = streaming;
        }

        public uint Source { get; }
        public BufferState? StaticBuffer { get; }
        public StreamingVoiceState? Streaming { get; }

        public static VoiceState ForStatic(uint source, BufferState buffer) => new(source, buffer, null);
        public static VoiceState ForStreaming(uint source, StreamingVoiceState streaming) => new(source, null, streaming);
    }

    private sealed class StreamingVoiceState(
        IAudioStreamSource decoder,
        uint[] buffers,
        byte[] decodeBuffer,
        bool loop) : IDisposable
    {
        public IAudioStreamSource Decoder { get; } = decoder;
        public uint[] Buffers { get; } = buffers;
        public byte[] DecodeBuffer { get; } = decodeBuffer;
        public bool Loop { get; set; } = loop;
        public int QueuedBuffers { get; set; }

        public void Dispose() => Decoder.Dispose();
    }

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
