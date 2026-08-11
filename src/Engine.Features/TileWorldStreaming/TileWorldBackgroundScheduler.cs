namespace GameEngine.Features.TileWorldStreaming;

/// <summary>
/// Bounds CPU-heavy archive verification and image decode work across every live and retiring
/// Level owned by one TileWorld session. Waiting operations remain asynchronously cancellable.
/// </summary>
internal sealed class TileWorldBackgroundScheduler : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private bool _disposed;

    public TileWorldBackgroundScheduler(int maximumConcurrency)
    {
        if (maximumConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _slots = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(work);
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _slots.Dispose();
    }
}
