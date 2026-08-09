namespace GameEngine.Core.Domain.ValueObjects;

using System.Collections.ObjectModel;

/// <summary>
/// Immutable Scene layer selection used by independently rendered views.
/// Construction validates and copies names once; <see cref="Allows"/> is allocation-free.
/// </summary>
public sealed class SceneLayerFilter : IEquatable<SceneLayerFilter>
{
    private readonly string[] _layerNames;
    private readonly ReadOnlyCollection<string> _readOnlyLayerNames;
    private readonly bool _includeMatches;

    public static SceneLayerFilter All { get; } = new(Array.Empty<string>(), includeMatches: false);

    public IReadOnlyList<string> LayerNames => _readOnlyLayerNames;
    public bool IsAll => _layerNames.Length == 0;
    public bool IsInclusive => !IsAll && _includeMatches;
    public bool IsExclusive => !IsAll && !_includeMatches;

    private SceneLayerFilter(string[] layerNames, bool includeMatches)
    {
        _layerNames = layerNames;
        _readOnlyLayerNames = Array.AsReadOnly(layerNames);
        _includeMatches = includeMatches;
    }

    public static SceneLayerFilter Include(params string[] layerNames) =>
        Create(layerNames, includeMatches: true);

    public static SceneLayerFilter Exclude(params string[] layerNames) =>
        Create(layerNames, includeMatches: false);

    public bool Allows(string layerName)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        if (_layerNames.Length == 0) return true;

        for (int i = 0; i < _layerNames.Length; i++)
        {
            if (string.Equals(_layerNames[i], layerName, StringComparison.Ordinal))
                return _includeMatches;
        }
        return !_includeMatches;
    }

    public override string ToString() => IsAll
        ? "All"
        : $"{(_includeMatches ? "Include" : "Exclude")}({string.Join(", ", _layerNames)})";

    public bool Equals(SceneLayerFilter? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _includeMatches != other._includeMatches ||
            _layerNames.Length != other._layerNames.Length)
            return false;
        for (int i = 0; i < _layerNames.Length; i++)
        {
            if (!string.Equals(_layerNames[i], other._layerNames[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is SceneLayerFilter other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_includeMatches);
        for (int i = 0; i < _layerNames.Length; i++)
            hash.Add(_layerNames[i], StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public static bool operator ==(SceneLayerFilter? left, SceneLayerFilter? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SceneLayerFilter? left, SceneLayerFilter? right) => !(left == right);

    private static SceneLayerFilter Create(string[] layerNames, bool includeMatches)
    {
        ArgumentNullException.ThrowIfNull(layerNames);
        if (layerNames.Length == 0)
            throw new ArgumentException("At least one Scene layer name is required.", nameof(layerNames));

        var copy = new string[layerNames.Length];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < layerNames.Length; i++)
        {
            string name = layerNames[i] ?? throw new ArgumentException(
                "Scene layer names cannot be null.", nameof(layerNames));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Scene layer names cannot be empty.", nameof(layerNames));
            if (!unique.Add(name))
                throw new ArgumentException(
                    $"Scene layer '{name}' is selected more than once.", nameof(layerNames));
            copy[i] = name;
        }
        Array.Sort(copy, StringComparer.Ordinal);
        return new SceneLayerFilter(copy, includeMatches);
    }
}
