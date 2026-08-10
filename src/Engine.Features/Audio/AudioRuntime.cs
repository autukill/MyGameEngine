namespace GameEngine.Features.Audio;

public sealed class AudioRuntime : IDisposable
{
    private struct VoiceSlot
    {
        public uint Generation;
        public bool Active;
        public AudioBackendVoice BackendVoice;
        public AudioClipRef Clip;
        public AudioBusRef Bus;
        public float Volume;
        public float Pan;
        public float Pitch;
        public bool Loop;
        public int Priority;
        public long StartSequence;
    }

    private struct BusState
    {
        public float Volume;
        public bool Muted;
    }

    private readonly AudioLibrary _library;
    private readonly IAudioBackend _backend;
    private readonly bool _ownsBackend;
    private readonly VoiceSlot[] _voices;
    private readonly Dictionary<AudioBusRef, BusState> _buses = [];
    private long _startSequence;
    private bool _disposed;

    public AudioRuntime(AudioLibrary library, IAudioBackend backend, int maxVoices = 32, bool ownsBackend = false)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (maxVoices <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxVoices));

        _ownsBackend = ownsBackend;
        _voices = new VoiceSlot[maxVoices];
        RegisterBus(AudioBusRef.Master);
        RegisterBus(AudioBusRef.Music);
        RegisterBus(AudioBusRef.Sfx);
    }

    public int Capacity => _voices.Length;

    public int ActiveVoiceCount { get; private set; }

    public void RegisterBus(AudioBusRef bus)
    {
        ThrowIfDisposed();
        if (bus.IsEmpty)
            throw new ArgumentException("Audio bus reference cannot be empty.", nameof(bus));
        if (!_buses.TryAdd(bus, new BusState { Volume = 1f }))
            throw new ArgumentException($"Audio bus '{bus}' is already registered.", nameof(bus));
    }

    public void SetBusVolume(AudioBusRef bus, float volume)
    {
        ValidateUnit(volume, nameof(volume));
        BusState state = GetBus(bus);
        state.Volume = volume;
        _buses[bus] = state;
        if (bus == AudioBusRef.Master)
            RefreshAll();
        else
            RefreshBus(bus);
    }

    public void SetBusMuted(AudioBusRef bus, bool muted)
    {
        BusState state = GetBus(bus);
        state.Muted = muted;
        _buses[bus] = state;
        if (bus == AudioBusRef.Master)
            RefreshAll();
        else
            RefreshBus(bus);
    }

    public bool TryPlay(AudioClipRef clip, in AudioPlayOptions options, out AudioVoiceRef voice)
    {
        ThrowIfDisposed();
        ValidateOptions(options);
        AudioClipDescriptor descriptor = _library.Get(clip);
        _ = GetBus(options.Bus);

        int slot = FindAvailableSlot();
        if (slot < 0)
        {
            slot = FindVoiceToSteal(options.Priority);
            if (slot < 0)
            {
                voice = default;
                return false;
            }

            StopSlot(slot);
        }

        ref VoiceSlot entry = ref _voices[slot];
        entry.Generation = NextGeneration(entry.Generation);
        entry.Clip = clip;
        entry.Bus = options.Bus;
        entry.Volume = options.Volume;
        entry.Pan = options.Pan;
        entry.Pitch = options.Pitch;
        entry.Loop = options.Loop;
        entry.Priority = options.Priority;
        entry.StartSequence = checked(++_startSequence);

        AudioVoiceMix mix = BuildMix(in entry);
        entry.BackendVoice = _backend.Play(in descriptor, in mix);
        if (entry.BackendVoice.IsEmpty)
            throw new InvalidOperationException("Audio backend returned an empty voice handle.");

        entry.Active = true;
        ActiveVoiceCount++;
        voice = new AudioVoiceRef(slot, entry.Generation);
        return true;
    }

    public AudioVoiceRef Play(AudioClipRef clip, in AudioPlayOptions options)
    {
        if (!TryPlay(clip, in options, out AudioVoiceRef voice))
            throw new InvalidOperationException("No audio voice is available at the requested priority.");
        return voice;
    }

    public bool IsPlaying(AudioVoiceRef voice)
    {
        ThrowIfDisposed();
        return TryResolve(voice, out int slot) && _backend.IsPlaying(_voices[slot].BackendVoice);
    }

    public bool Stop(AudioVoiceRef voice)
    {
        ThrowIfDisposed();
        if (!TryResolve(voice, out int slot))
            return false;

        StopSlot(slot);
        return true;
    }

    public bool SetVoiceVolume(AudioVoiceRef voice, float volume)
    {
        ThrowIfDisposed();
        ValidateUnit(volume, nameof(volume));
        if (!TryResolve(voice, out int slot))
            return false;

        _voices[slot].Volume = volume;
        RefreshVoice(slot);
        return true;
    }

    public bool TryGetSnapshot(AudioVoiceRef voice, out AudioVoiceSnapshot snapshot)
    {
        ThrowIfDisposed();
        if (!TryResolve(voice, out int slot))
        {
            snapshot = default;
            return false;
        }

        ref VoiceSlot entry = ref _voices[slot];
        snapshot = new AudioVoiceSnapshot(
            voice,
            entry.Clip,
            entry.Bus,
            entry.Volume,
            entry.Pan,
            entry.Pitch,
            entry.Loop,
            entry.Priority,
            entry.StartSequence);
        return true;
    }

    public void Update()
    {
        ThrowIfDisposed();
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active && !_backend.IsPlaying(_voices[i].BackendVoice))
                ReleaseSlot(i);
        }
    }

    public void StopAll()
    {
        ThrowIfDisposed();
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active)
                StopSlot(i);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active)
                StopSlot(i);
        }

        _disposed = true;
        if (_ownsBackend)
            _backend.Dispose();
    }

    private int FindAvailableSlot()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active)
                return i;
        }
        return -1;
    }

    private int FindVoiceToSteal(int requestedPriority)
    {
        var candidate = -1;
        for (var i = 0; i < _voices.Length; i++)
        {
            ref VoiceSlot current = ref _voices[i];
            if (current.Priority > requestedPriority)
                continue;
            if (candidate < 0 || current.Priority < _voices[candidate].Priority ||
                current.Priority == _voices[candidate].Priority && current.StartSequence < _voices[candidate].StartSequence)
                candidate = i;
        }
        return candidate;
    }

    private bool TryResolve(AudioVoiceRef voice, out int slot)
    {
        slot = voice.Slot;
        return !voice.IsEmpty && (uint)slot < (uint)_voices.Length &&
               _voices[slot].Active && _voices[slot].Generation == voice.Generation;
    }

    private void StopSlot(int slot)
    {
        _backend.Stop(_voices[slot].BackendVoice);
        ReleaseSlot(slot);
    }

    private void ReleaseSlot(int slot)
    {
        ref VoiceSlot entry = ref _voices[slot];
        entry.Active = false;
        entry.BackendVoice = default;
        entry.Clip = default;
        entry.Bus = default;
        ActiveVoiceCount--;
    }

    private void RefreshAll()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active)
                RefreshVoice(i);
        }
    }

    private void RefreshBus(AudioBusRef bus)
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            if (_voices[i].Active && _voices[i].Bus == bus)
                RefreshVoice(i);
        }
    }

    private void RefreshVoice(int slot)
    {
        AudioVoiceMix mix = BuildMix(in _voices[slot]);
        _backend.SetMix(_voices[slot].BackendVoice, in mix);
    }

    private AudioVoiceMix BuildMix(in VoiceSlot voice)
    {
        BusState master = _buses[AudioBusRef.Master];
        BusState bus = _buses[voice.Bus];
        float volume = master.Muted || bus.Muted ? 0f : voice.Volume * master.Volume * bus.Volume;
        return new AudioVoiceMix(volume, voice.Pan, voice.Pitch, voice.Loop);
    }

    private BusState GetBus(AudioBusRef bus)
    {
        ThrowIfDisposed();
        if (bus.IsEmpty || !_buses.TryGetValue(bus, out BusState state))
            throw new KeyNotFoundException($"Audio bus '{bus}' is not registered.");
        return state;
    }

    private static void ValidateOptions(in AudioPlayOptions options)
    {
        if (options.Bus.IsEmpty)
            throw new ArgumentException("Audio play options require a bus.", nameof(options));
        ValidateUnit(options.Volume, nameof(options.Volume));
        if (!float.IsFinite(options.Pan) || options.Pan is < -1f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(options), "Audio pan must be within [-1, 1].");
        if (!float.IsFinite(options.Pitch) || options.Pitch is < 0.25f or > 4f)
            throw new ArgumentOutOfRangeException(nameof(options), "Audio pitch must be within [0.25, 4].");
    }

    private static void ValidateUnit(float value, string paramName)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite and within [0, 1].");
    }

    private static uint NextGeneration(uint current) => current == uint.MaxValue ? 1u : current + 1u;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
