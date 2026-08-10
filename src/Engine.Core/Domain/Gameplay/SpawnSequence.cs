namespace GameEngine.Core.Domain.Gameplay;

/// <summary>Selects whether a completed Spawn sequence stops or begins its timeline again.</summary>
public enum SpawnSequenceRepeat
{
    Once,
    Loop
}

/// <summary>
/// Identifies one deterministic emission in a Spawn sequence. Games decide what the emission
/// creates, so the timeline remains independent from Prefabs and concrete Instance types.
/// </summary>
public readonly record struct SpawnEmission(
    long SequenceIteration,
    int WaveIndex,
    int ItemIndex,
    long TotalEmissionIndex);

/// <summary>
/// Receives a scheduled emission. One callback consumes one concurrency slot even when the
/// created Instance remains queued until the Scene's safe mutation boundary.
/// </summary>
public delegate void SpawnEmissionHandler(in SpawnEmission emission);

/// <summary>Serializable value snapshot for deterministic replay and gameplay diagnostics.</summary>
public readonly record struct SpawnSequencePlayerState(
    int SegmentIndex,
    int ItemIndex,
    int WaveIndex,
    long Iteration,
    long TotalEmissions,
    double RemainingSeconds,
    bool WaitingAtLoopBoundary,
    bool IsCompleted);

/// <summary>
/// Immutable authoring plan made of explicit delays and finite waves. Build it once during game
/// construction, then give it to one or more independently stateful SpawnSequencePlayers.
/// </summary>
public sealed class SpawnSequence
{
    private readonly SpawnSequenceSegment[] _segments;

    internal SpawnSequence(
        SpawnSequenceSegment[] segments,
        SpawnSequenceRepeat repeat,
        int maximumConcurrent)
    {
        _segments = segments;
        Repeat = repeat;
        MaximumConcurrent = maximumConcurrent;
    }

    public SpawnSequenceRepeat Repeat { get; }
    public int MaximumConcurrent { get; }
    public int SegmentCount => _segments.Length;
    public int WaveCount { get; internal init; }

    internal SpawnSequenceSegment GetSegment(int index) => _segments[index];
}

/// <summary>
/// Construction-time fluent builder. It allocates only while authoring and freezes all segments
/// into an immutable SpawnSequence at Build.
/// </summary>
public sealed class SpawnSequenceBuilder
{
    private readonly List<SpawnSequenceSegment> _segments = [];
    private int _waveCount;

    /// <summary>Adds a pause before the following timeline segment.</summary>
    public SpawnSequenceBuilder Delay(double seconds)
    {
        ValidateDuration(seconds, nameof(seconds), allowZero: false);
        _segments.Add(SpawnSequenceSegment.Delay(seconds));
        return this;
    }

    /// <summary>
    /// Adds a finite wave. Its first item is ready immediately when the wave is entered; interval
    /// is the delay between subsequent items.
    /// </summary>
    public SpawnSequenceBuilder Wave(int count, double intervalSeconds)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                "Wave count must be greater than zero.");
        ValidateDuration(intervalSeconds, nameof(intervalSeconds), allowZero: true);
        _segments.Add(SpawnSequenceSegment.Wave(count, intervalSeconds, _waveCount++));
        return this;
    }

    public SpawnSequence Build(
        SpawnSequenceRepeat repeat = SpawnSequenceRepeat.Once,
        int maximumConcurrent = int.MaxValue)
    {
        if (!Enum.IsDefined(repeat))
            throw new ArgumentOutOfRangeException(nameof(repeat), repeat,
                "Unknown Spawn sequence repeat mode.");
        if (maximumConcurrent <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrent), maximumConcurrent,
                "Maximum concurrent count must be greater than zero.");
        if (_segments.Count == 0 || _waveCount == 0)
            throw new InvalidOperationException("A Spawn sequence must contain at least one wave.");
        if (repeat == SpawnSequenceRepeat.Loop && !HasPositiveLoopDuration())
            throw new InvalidOperationException(
                "A looping Spawn sequence needs a positive delay or wave interval.");

        return new SpawnSequence([.. _segments], repeat, maximumConcurrent)
        {
            WaveCount = _waveCount
        };
    }

    private bool HasPositiveLoopDuration()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            SpawnSequenceSegment segment = _segments[i];
            if (segment.DurationSeconds > 0d) return true;
        }
        return false;
    }

    private static void ValidateDuration(double value, string parameter, bool allowZero)
    {
        if (!double.IsFinite(value) || (allowZero ? value < 0d : value <= 0d))
            throw new ArgumentOutOfRangeException(
                parameter, value,
                allowZero
                    ? "Duration must be finite and non-negative."
                    : "Duration must be finite and greater than zero.");
    }
}

/// <summary>
/// Owner-driven deterministic Spawn timeline. Calling Update from GameInstance.OnStep naturally
/// inherits the owner's active, pause, time-scale, and InstanceTimeMode semantics. Updates allocate
/// no managed memory after construction when the caller caches its emission delegate.
/// </summary>
public sealed class SpawnSequencePlayer
{
    private const double TimelineEpsilon = 1e-12d;
    private readonly SpawnSequence _sequence;
    private int _segmentIndex;
    private int _itemIndex;
    private int _waveIndex;
    private long _iteration;
    private long _totalEmissions;
    private double _remainingSeconds;
    private bool _waitingAtLoopBoundary;

    public SpawnSequencePlayer(SpawnSequence sequence)
    {
        _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        EnterSegment(0);
    }

    public SpawnSequence Sequence => _sequence;
    public bool IsCompleted { get; private set; }
    public bool IsWaitingForCapacity { get; private set; }
    public int CurrentWaveIndex => _waveIndex;
    public int CurrentItemIndex => _itemIndex;
    public long Iteration => _iteration;
    public long TotalEmissions => _totalEmissions;
    public double RemainingSeconds => _remainingSeconds;

    /// <summary>
    /// Advances with time already selected by the owner. activeCount must count committed live
    /// instances; emissions made earlier in this call are counted locally until Scene commit.
    /// Returns the number of callbacks made.
    /// </summary>
    public int Update(
        double deltaTime,
        int activeCount,
        SpawnEmissionHandler emit)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime,
                "Delta time must be finite and non-negative.");
        if (activeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(activeCount), activeCount,
                "Active count must be non-negative.");
        ArgumentNullException.ThrowIfNull(emit);

        IsWaitingForCapacity = false;
        if (IsCompleted) return 0;

        int emitted = 0;
        double available = deltaTime;
        while (!IsCompleted)
        {
            if (_remainingSeconds > 0d)
            {
                if (available + TimelineEpsilon < _remainingSeconds)
                {
                    _remainingSeconds -= available;
                    return emitted;
                }
                available = Math.Max(0d, available - _remainingSeconds);
                _remainingSeconds = 0d;
            }

            if (_waitingAtLoopBoundary)
            {
                _waitingAtLoopBoundary = false;
                AdvanceSegment();
                continue;
            }

            SpawnSequenceSegment segment = _sequence.GetSegment(_segmentIndex);
            if (segment.Kind == SpawnSequenceSegmentKind.Delay)
            {
                AdvanceSegment();
                continue;
            }

            if (activeCount + emitted >= _sequence.MaximumConcurrent)
            {
                IsWaitingForCapacity = true;
                return emitted;
            }

            var emission = new SpawnEmission(
                _iteration,
                segment.WaveIndex,
                _itemIndex,
                _totalEmissions);
            emit(in emission);
            emitted++;
            _totalEmissions++;
            _itemIndex++;

            if (_itemIndex < segment.Count)
            {
                _remainingSeconds = segment.DurationSeconds;
                continue;
            }

            bool finalSegment = _segmentIndex == _sequence.SegmentCount - 1;
            if (finalSegment && _sequence.Repeat == SpawnSequenceRepeat.Loop &&
                segment.DurationSeconds > 0d)
            {
                // Preserve the authored cadence between the final emission and the next loop.
                _remainingSeconds = segment.DurationSeconds;
                _waitingAtLoopBoundary = true;
                continue;
            }
            AdvanceSegment();
        }
        return emitted;
    }

    /// <summary>Stops either a finite or looping sequence. Future Update calls are no-ops.</summary>
    public void Complete()
    {
        IsCompleted = true;
        IsWaitingForCapacity = false;
        _remainingSeconds = 0d;
        _waitingAtLoopBoundary = false;
    }

    /// <summary>Restarts from the first authored segment and clears all counters.</summary>
    public void Restart()
    {
        IsCompleted = false;
        IsWaitingForCapacity = false;
        _segmentIndex = 0;
        _itemIndex = 0;
        _waveIndex = 0;
        _iteration = 0;
        _totalEmissions = 0;
        _waitingAtLoopBoundary = false;
        EnterSegment(0);
    }

    public SpawnSequencePlayerState CaptureState() => new(
        _segmentIndex,
        _itemIndex,
        _waveIndex,
        _iteration,
        _totalEmissions,
        _remainingSeconds,
        _waitingAtLoopBoundary,
        IsCompleted);

    public void RestoreState(in SpawnSequencePlayerState state)
    {
        if (state.SegmentIndex < 0 || state.SegmentIndex >= _sequence.SegmentCount ||
            state.ItemIndex < 0 || state.WaveIndex < 0 || state.WaveIndex >= _sequence.WaveCount ||
            state.Iteration < 0 || state.TotalEmissions < 0 ||
            !double.IsFinite(state.RemainingSeconds) || state.RemainingSeconds < 0d)
            throw new ArgumentException("Spawn sequence player state is invalid.", nameof(state));

        SpawnSequenceSegment segment = _sequence.GetSegment(state.SegmentIndex);
        int expectedWaveIndex = segment.Kind == SpawnSequenceSegmentKind.Wave
            ? segment.WaveIndex
            : FindFollowingWave(state.SegmentIndex);
        bool invalidCompletedState = state.IsCompleted &&
            (state.WaitingAtLoopBoundary || state.RemainingSeconds != 0d);
        bool invalidDelayState = segment.Kind == SpawnSequenceSegmentKind.Delay &&
            (state.ItemIndex != 0 || state.WaitingAtLoopBoundary ||
             state.RemainingSeconds > segment.DurationSeconds);
        bool validLoopBoundary = !state.IsCompleted &&
            _sequence.Repeat == SpawnSequenceRepeat.Loop &&
            state.SegmentIndex == _sequence.SegmentCount - 1 &&
            segment.Kind == SpawnSequenceSegmentKind.Wave &&
            segment.DurationSeconds > 0d &&
            state.ItemIndex == segment.Count &&
            state.RemainingSeconds > 0d &&
            state.RemainingSeconds <= segment.DurationSeconds;
        bool invalidWaveState = segment.Kind == SpawnSequenceSegmentKind.Wave &&
            (state.ItemIndex > segment.Count ||
             state.RemainingSeconds > segment.DurationSeconds ||
             state.WaitingAtLoopBoundary != validLoopBoundary ||
             (!state.IsCompleted && state.ItemIndex == segment.Count && !validLoopBoundary));
        if (state.WaveIndex != expectedWaveIndex || invalidCompletedState ||
            invalidDelayState || invalidWaveState ||
            (_sequence.Repeat == SpawnSequenceRepeat.Once && state.Iteration != 0))
            throw new ArgumentException(
                "Spawn sequence player state is inconsistent with its authored timeline.",
                nameof(state));

        _segmentIndex = state.SegmentIndex;
        _itemIndex = state.ItemIndex;
        _waveIndex = state.WaveIndex;
        _iteration = state.Iteration;
        _totalEmissions = state.TotalEmissions;
        _remainingSeconds = state.RemainingSeconds;
        _waitingAtLoopBoundary = state.WaitingAtLoopBoundary;
        IsCompleted = state.IsCompleted;
        IsWaitingForCapacity = false;
    }

    private void AdvanceSegment()
    {
        int next = _segmentIndex + 1;
        if (next < _sequence.SegmentCount)
        {
            EnterSegment(next);
            return;
        }

        if (_sequence.Repeat == SpawnSequenceRepeat.Once)
        {
            Complete();
            return;
        }

        _iteration++;
        EnterSegment(0);
    }

    private void EnterSegment(int index)
    {
        _segmentIndex = index;
        _itemIndex = 0;
        SpawnSequenceSegment segment = _sequence.GetSegment(index);
        _waveIndex = segment.Kind == SpawnSequenceSegmentKind.Wave
            ? segment.WaveIndex
            : FindFollowingWave(index);
        _remainingSeconds = segment.Kind == SpawnSequenceSegmentKind.Delay
            ? segment.DurationSeconds
            : 0d;
    }

    private int FindFollowingWave(int start)
    {
        for (int i = start + 1; i < _sequence.SegmentCount; i++)
        {
            SpawnSequenceSegment candidate = _sequence.GetSegment(i);
            if (candidate.Kind == SpawnSequenceSegmentKind.Wave)
                return candidate.WaveIndex;
        }
        return 0;
    }
}

internal enum SpawnSequenceSegmentKind
{
    Delay,
    Wave
}

internal readonly record struct SpawnSequenceSegment(
    SpawnSequenceSegmentKind Kind,
    double DurationSeconds,
    int Count,
    int WaveIndex)
{
    public static SpawnSequenceSegment Delay(double seconds) =>
        new(SpawnSequenceSegmentKind.Delay, seconds, 0, -1);

    public static SpawnSequenceSegment Wave(int count, double intervalSeconds, int waveIndex) =>
        new(SpawnSequenceSegmentKind.Wave, intervalSeconds, count, waveIndex);
}
