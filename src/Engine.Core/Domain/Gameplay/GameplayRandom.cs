namespace GameEngine.Core.Domain.Gameplay;

using GameEngine.Core.Domain.ValueObjects;

/// <summary>Serializable value needed to resume one deterministic random stream exactly.</summary>
public readonly record struct GameplayRandomState(ulong Value);

/// <summary>
/// Owner-local deterministic PCG32 random stream. The bit sequence is stable across supported
/// .NET versions and operating systems; the object is intentionally mutable and not thread-safe.
/// </summary>
public sealed class GameplayRandom
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Increment = 1442695040888963407UL;
    private const float UInt24Scale = 1f / 16777216f;
    private ulong _state;

    public const int AlgorithmVersion = 1;

    public GameplayRandom(ulong seed) => Reset(seed);

    public uint NextUInt()
    {
        ulong previous = _state;
        _state = unchecked(previous * Multiplier + Increment);
        uint mixed = (uint)(((previous >> 18) ^ previous) >> 27);
        int rotation = (int)(previous >> 59);
        return (mixed >> rotation) | (mixed << ((-rotation) & 31));
    }

    public int NextInt(int maximumExclusive)
    {
        if (maximumExclusive <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive), maximumExclusive,
                "Exclusive maximum must be positive.");
        return (int)NextBoundedUInt((uint)maximumExclusive);
    }

    public int Range(int minimumInclusive, int maximumExclusive)
    {
        if (minimumInclusive >= maximumExclusive)
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive), maximumExclusive,
                "Exclusive maximum must be greater than the inclusive minimum.");
        uint width = (uint)((long)maximumExclusive - minimumInclusive);
        return (int)(minimumInclusive + (long)NextBoundedUInt(width));
    }

    /// <summary>Returns a value in [0, 1) using the high 24 random bits.</summary>
    public float NextFloat() => (NextUInt() >> 8) * UInt24Scale;

    public float Range(float minimumInclusive, float maximumExclusive)
    {
        ValidateFinite(minimumInclusive, nameof(minimumInclusive));
        ValidateFinite(maximumExclusive, nameof(maximumExclusive));
        if (minimumInclusive > maximumExclusive)
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive), maximumExclusive,
                "Maximum must be greater than or equal to minimum.");

        if (minimumInclusive == maximumExclusive) return minimumInclusive;
        float width = maximumExclusive - minimumInclusive;
        if (!float.IsFinite(width))
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive), maximumExclusive,
                "The requested floating-point range is too wide.");
        float unit = NextFloat();
        float value = minimumInclusive + width * unit;
        return value < maximumExclusive ? value : MathF.BitDecrement(maximumExclusive);
    }

    public bool Chance(float probability)
    {
        if (!float.IsFinite(probability) || probability < 0f || probability > 1f)
            throw new ArgumentOutOfRangeException(
                nameof(probability), probability,
                "Probability must be finite and within [0, 1].");
        return NextFloat() < probability;
    }

    public Vector2D Direction2D()
    {
        float angle = NextFloat() * MathF.Tau;
        return new Vector2D(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>Returns a uniformly distributed point inside a circle centered at zero.</summary>
    public Vector2D InsideCircle(float radius)
    {
        if (!float.IsFinite(radius) || radius < 0f)
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius,
                "Circle radius must be finite and non-negative.");
        Vector2D direction = Direction2D();
        float distance = MathF.Sqrt(NextFloat()) * radius;
        return direction * distance;
    }

    public T Choose<T>(ReadOnlySpan<T> values)
    {
        if (values.IsEmpty)
            throw new ArgumentException("Cannot choose from an empty span.", nameof(values));
        return values[NextInt(values.Length)];
    }

    public void Shuffle<T>(Span<T> values)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int other = NextInt(i + 1);
            (values[i], values[other]) = (values[other], values[i]);
        }
    }

    public GameplayRandomState CaptureState() => new(_state);

    public void RestoreState(GameplayRandomState state) => _state = state.Value;

    public void Reset(ulong seed)
    {
        _state = 0UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    private uint NextBoundedUInt(uint bound)
    {
        uint threshold = unchecked(0U - bound) % bound;
        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold) return value % bound;
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(
                parameterName, value,
                "Random range bounds must be finite.");
    }
}
