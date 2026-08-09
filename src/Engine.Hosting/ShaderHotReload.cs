namespace GameEngine.Hosting;

using System.Security.Cryptography;
using System.Text;
using GameEngine.Core.Domain.Graphics;
using GameEngine.Core.Infrastructure.Graphics;

public sealed record ShaderFileDefinition
{
    public ShaderFileDefinition(string name, string vertexPath, string fragmentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidateRelativePath(vertexPath, nameof(vertexPath));
        ValidateRelativePath(fragmentPath, nameof(fragmentPath));
        Name = name;
        VertexPath = vertexPath;
        FragmentPath = fragmentPath;
    }

    public string Name { get; }
    public string VertexPath { get; }
    public string FragmentPath { get; }

    private static void ValidateRelativePath(string path, string parameter)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new ArgumentException("Shader paths must be non-empty and relative.", parameter);
    }
}

public enum ShaderHotReloadStatus
{
    Detected,
    Applied,
    Failed
}

public sealed record ShaderHotReloadDiagnostic(
    ShaderHotReloadStatus Status,
    IReadOnlyList<string> ShaderNames,
    string? Fingerprint,
    TimeSpan Duration,
    string? Error = null);

public interface IShaderHotReloadSink
{
    void Publish(ShaderHotReloadDiagnostic diagnostic);
}

public sealed record ShaderHotReloadOptions
{
    public ShaderHotReloadOptions(
        IShaderHotReloadSink sink,
        TimeSpan? pollInterval = null,
        TimeSpan? debounce = null)
    {
        Sink = sink ?? throw new ArgumentNullException(nameof(sink));
        PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        Debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        if (PollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (Debounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounce));
    }

    public IShaderHotReloadSink Sink { get; }
    public TimeSpan PollInterval { get; }
    public TimeSpan Debounce { get; }
}

internal sealed record ShaderFileSetSnapshot(
    string Fingerprint,
    IReadOnlyDictionary<string, string> ProgramFingerprints,
    IReadOnlyList<ShaderProgramSource> Sources)
{
    public string[] ChangedNamesFrom(ShaderFileSetSnapshot previous) => Sources
        .Where(source => !previous.ProgramFingerprints.TryGetValue(source.Name, out string? fingerprint) ||
                         !StringComparer.Ordinal.Equals(
                             fingerprint,
                             ProgramFingerprints[source.Name]))
        .Select(source => source.Name)
        .ToArray();
}

internal static class ShaderFileSetReader
{
    public static ShaderFileSetSnapshot Read(
        string root,
        IReadOnlyList<ShaderFileDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(definitions);
        string fullRoot = Path.GetFullPath(root);
        ShaderFileSetSnapshot first = ReadOnce(fullRoot, definitions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ShaderFileSetSnapshot second = ReadOnce(fullRoot, definitions, cancellationToken);
        if (!StringComparer.Ordinal.Equals(first.Fingerprint, second.Fingerprint))
            throw new IOException("Shader files changed while a stable source snapshot was being read.");
        return first;
    }

    private static ShaderFileSetSnapshot ReadOnce(
        string fullRoot,
        IReadOnlyList<ShaderFileDefinition> definitions,
        CancellationToken cancellationToken)
    {
        var sources = new List<ShaderProgramSource>(definitions.Count);
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ShaderFileDefinition definition in definitions.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string vertex = File.ReadAllText(ResolveUnderRoot(fullRoot, definition.VertexPath));
            string fragment = File.ReadAllText(ResolveUnderRoot(fullRoot, definition.FragmentPath));
            string fingerprint = Hash(definition.Name, vertex, fragment);
            if (!fingerprints.TryAdd(definition.Name, fingerprint))
                throw new InvalidDataException($"Shader '{definition.Name}' is configured more than once.");
            sources.Add(new ShaderProgramSource(definition.Name, vertex, fragment));
        }

        string combined = Hash(string.Join('\n', fingerprints
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value}")));
        return new ShaderFileSetSnapshot(combined, fingerprints, sources);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, path);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException($"Shader path '{relativePath}' escapes its configured root.");
        return path;
    }

    private static string Hash(params string[] values)
    {
        string joined = string.Join('\0', values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))
            .ToLowerInvariant();
    }
}

internal sealed class ShaderHotReloadCoordinator : IDisposable
{
    private readonly ShaderLibrary _library;
    private readonly string _root;
    private readonly IReadOnlyList<ShaderFileDefinition> _definitions;
    private readonly ShaderHotReloadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cancellation = new();
    private ShaderFileSetSnapshot _active;
    private ShaderFileSetSnapshot? _candidate;
    private DateTimeOffset _candidateSince;
    private DateTimeOffset _nextPoll;
    private Task<ShaderFileSetSnapshot>? _poll;
    private long _pollStarted;
    private string? _failedMarker;
    private bool _disposed;

    public ShaderHotReloadCoordinator(
        ShaderLibrary library,
        string root,
        IReadOnlyList<ShaderFileDefinition> definitions,
        ShaderFileSetSnapshot active,
        ShaderHotReloadOptions options,
        TimeProvider? timeProvider = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _root = Path.GetFullPath(root);
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _active = active ?? throw new ArgumentNullException(nameof(active));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _nextPoll = _timeProvider.GetUtcNow() + options.PollInterval;
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompletePoll();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_poll is not null || now < _nextPoll) return;
        _nextPoll = now + _options.PollInterval;
        _pollStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _poll = Task.Run(
            () => ShaderFileSetReader.Read(_root, _definitions, _cancellation.Token),
            _cancellation.Token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void CompletePoll()
    {
        if (_poll is not { IsCompleted: true } task) return;
        _poll = null;
        TimeSpan duration = System.Diagnostics.Stopwatch.GetElapsedTime(_pollStarted);
        ShaderFileSetSnapshot observed;
        try
        {
            observed = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            return;
        }
        catch (Exception ex)
        {
            PublishFailure(null, Array.Empty<string>(), duration, ex);
            return;
        }

        if (observed.Fingerprint == _active.Fingerprint)
        {
            _candidate = null;
            _failedMarker = null;
            return;
        }
        if (StringComparer.Ordinal.Equals(observed.Fingerprint, _failedMarker)) return;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_candidate?.Fingerprint != observed.Fingerprint)
        {
            _candidate = observed;
            _candidateSince = now;
            _options.Sink.Publish(new ShaderHotReloadDiagnostic(
                ShaderHotReloadStatus.Detected,
                observed.ChangedNamesFrom(_active),
                observed.Fingerprint,
                TimeSpan.Zero));
            return;
        }
        if (now - _candidateSince < _options.Debounce) return;

        string[] changed = observed.ChangedNamesFrom(_active);
        try
        {
            var changedSet = changed.ToHashSet(StringComparer.Ordinal);
            ShaderProgramSource[] replacements = observed.Sources
                .Where(source => changedSet.Contains(source.Name))
                .ToArray();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            _library.ReplaceAll(replacements);
            duration += System.Diagnostics.Stopwatch.GetElapsedTime(started);
            _active = observed;
            _candidate = null;
            _failedMarker = null;
            _options.Sink.Publish(new ShaderHotReloadDiagnostic(
                ShaderHotReloadStatus.Applied,
                changed,
                observed.Fingerprint,
                duration));
        }
        catch (Exception ex)
        {
            _candidate = null;
            _failedMarker = observed.Fingerprint;
            PublishFailure(observed.Fingerprint, changed, duration, ex);
        }
    }

    private void PublishFailure(
        string? fingerprint,
        IReadOnlyList<string> names,
        TimeSpan duration,
        Exception error)
    {
        string marker = fingerprint ?? $"{error.GetType().Name}:{error.Message}";
        if (fingerprint is null && StringComparer.Ordinal.Equals(marker, _failedMarker)) return;
        _failedMarker = marker;
        _options.Sink.Publish(new ShaderHotReloadDiagnostic(
            ShaderHotReloadStatus.Failed,
            names,
            fingerprint,
            duration,
            error.Message));
    }
}
