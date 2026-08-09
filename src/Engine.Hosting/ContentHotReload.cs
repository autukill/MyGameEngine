namespace GameEngine.Hosting;

using System.Diagnostics;
using GameEngine.Features.ContentAssets.Domain;
using GameEngine.Features.ContentAssets.Infrastructure;

public enum ContentHotReloadStatus
{
    Detected,
    Applied,
    Failed
}

public sealed record ContentHotReloadDiagnostic(
    ContentHotReloadStatus Status,
    string PackageId,
    string? Fingerprint,
    TimeSpan Duration,
    string? Error = null);

public interface IContentHotReloadSink
{
    void Publish(ContentHotReloadDiagnostic diagnostic);
}

public sealed record ContentHotReloadOptions
{
    public ContentHotReloadOptions(
        IContentHotReloadSink sink,
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

    public IContentHotReloadSink Sink { get; }
    public TimeSpan PollInterval { get; }
    public TimeSpan Debounce { get; }
}

internal sealed class ContentHotReloadCoordinator : IDisposable
{
    private readonly ContentPackageManager _manager;
    private readonly ContentPackageRef _package;
    private readonly ContentHotReloadOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _cancellation = new();
    private CompiledContentRevision _activeRevision;
    private CompiledContentRevision? _candidate;
    private DateTimeOffset _candidateSince;
    private DateTimeOffset _nextPoll;
    private Task<PreparedContentPackageReload>? _preparation;
    private CompiledContentRevision? _preparingRevision;
    private long _preparationStarted;
    private string? _lastFailedFingerprint;
    private bool _disposed;

    public ContentHotReloadCoordinator(
        ContentPackageManager manager,
        ContentPackageRef package,
        ContentHotReloadOptions options,
        TimeProvider? timeProvider = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _package = package;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _activeRevision = CompiledContentRevisionReader.Read(manager.PackagesRoot, package);
        _nextPoll = _timeProvider.GetUtcNow() + options.PollInterval;
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompletePreparation();

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now < _nextPoll) return;
        _nextPoll = now + _options.PollInterval;

        CompiledContentRevision observed;
        try
        {
            observed = CompiledContentRevisionReader.Read(_manager.PackagesRoot, _package);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            PublishFailure(null, TimeSpan.Zero, ex);
            return;
        }

        if (observed == _activeRevision)
        {
            _candidate = null;
            _lastFailedFingerprint = null;
            return;
        }
        if (_preparation is not null) return;
        if (StringComparer.Ordinal.Equals(observed.Fingerprint, _lastFailedFingerprint)) return;

        if (_candidate != observed)
        {
            _candidate = observed;
            _candidateSince = now;
            _options.Sink.Publish(new ContentHotReloadDiagnostic(
                ContentHotReloadStatus.Detected,
                _package.Id,
                observed.Fingerprint,
                TimeSpan.Zero));
            return;
        }
        if (now - _candidateSince < _options.Debounce) return;

        _preparationStarted = Stopwatch.GetTimestamp();
        _preparingRevision = observed;
        _preparation = _manager.PrepareReloadAsync(
            _package,
            observed,
            _cancellation.Token);
        _candidate = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void CompletePreparation()
    {
        if (_preparation is not { IsCompleted: true } task) return;
        _preparation = null;
        CompiledContentRevision? preparingRevision = _preparingRevision;
        _preparingRevision = null;
        TimeSpan duration = Stopwatch.GetElapsedTime(_preparationStarted);
        try
        {
            PreparedContentPackageReload prepared = task.GetAwaiter().GetResult();
            _manager.CommitReload(prepared);
            _activeRevision = prepared.Revision;
            _lastFailedFingerprint = null;
            _options.Sink.Publish(new ContentHotReloadDiagnostic(
                ContentHotReloadStatus.Applied,
                _package.Id,
                prepared.Revision.Fingerprint,
                duration));
        }
        catch (OperationCanceledException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            string? fingerprint = preparingRevision?.Fingerprint;
            if (task.Status == TaskStatus.RanToCompletion)
                fingerprint = task.Result.Revision.Fingerprint;
            _lastFailedFingerprint = fingerprint;
            PublishFailure(fingerprint, duration, ex);
        }
    }

    private void PublishFailure(string? fingerprint, TimeSpan duration, Exception error)
    {
        string marker = fingerprint ?? $"{error.GetType().Name}:{error.Message}";
        if (duration == TimeSpan.Zero && StringComparer.Ordinal.Equals(marker, _lastFailedFingerprint))
            return;
        _lastFailedFingerprint = marker;
        _options.Sink.Publish(new ContentHotReloadDiagnostic(
            ContentHotReloadStatus.Failed,
            _package.Id,
            fingerprint,
            duration,
            error.Message));
    }
}
