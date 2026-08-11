namespace GameEngine.Features.WorldStreaming;

using GameEngine.Features.ViewportNavigation;

/// <summary>
/// Main-thread coordinator for asynchronous chunk leases. Loader work may complete on any thread;
/// state transitions and events occur only when <see cref="Update"/> is called.
/// </summary>
public sealed class WorldChunkStreamer<TChunk> : IDisposable
    where TChunk : class, IDisposable
{
    private readonly IWorldChunkLoader<TChunk> _loader;
    private readonly Dictionary<WorldChunkCoordinate, Entry> _entries = [];
    private readonly List<WorldChunkCoordinate> _removeScratch = [];
    private WorldChunkRange _visible;
    private WorldChunkRange _preloaded;
    private WorldChunkRange _retained;
    private bool _hasDesiredSet;
    private bool _retiring;
    private bool _disposed;
    private int _activeLoads;

    public WorldChunkLayout Layout { get; }
    public WorldChunkStreamingOptions Options { get; }
    public ulong LastViewportRevision { get; private set; }
    public int TrackedCount => _entries.Count;
    public int ActiveLoadCount => _activeLoads;
    public bool IsRetiring => _retiring;

    public event Action<WorldChunkLoadedEvent>? ChunkLoaded;
    public event Action<WorldChunkUnloadedEvent>? ChunkUnloaded;
    public event Action<WorldChunkFailedEvent>? ChunkFailed;

    public WorldChunkStreamer(
        WorldChunkLayout layout,
        IWorldChunkLoader<TChunk> loader,
        WorldChunkStreamingOptions? options = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        Layout = layout;
        Options = options ?? WorldChunkStreamingOptions.Default;
    }

    public WorldChunkUpdateResult Update(in ViewportSnapshot viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retiring)
            throw new InvalidOperationException(
                "A retiring WorldChunkStreamer cannot accept new Viewport updates.");
        ValidateViewport(viewport);
        int completed = 0;
        int failures = 0;
        int unloaded = 0;
        HarvestCompleted(ref completed, ref failures, ref unloaded);

        bool changed = !_hasDesiredSet || viewport.Revision != LastViewportRevision;
        if (changed)
        {
            WorldChunkRange visible = Layout.GetRange(viewport.VisibleWorldBounds);
            WorldChunkRange preloaded = Layout.GetRange(
                viewport.VisibleWorldBounds,
                Options.PreloadMarginChunks);
            WorldChunkRange retained = Layout.GetRange(
                viewport.VisibleWorldBounds,
                Options.RetainMarginChunks);
            if (retained.Count > Options.MaximumTrackedChunks)
                throw new InvalidOperationException(
                    $"Viewport requires {retained.Count:N0} retained chunks, exceeding the " +
                    $"configured maximum of {Options.MaximumTrackedChunks:N0}.");
            _visible = visible;
            _preloaded = preloaded;
            _retained = retained;
            _hasDesiredSet = true;
            LastViewportRevision = viewport.Revision;
            ReconcileDesired(ref unloaded);
        }

        int remainingStarts = Options.MaximumLoadsStartedPerUpdate;
        int started = StartDesiredLoads(_visible, ref remainingStarts);
        if (_activeLoads < Options.MaximumConcurrentLoads && remainingStarts > 0)
            started += StartDesiredLoads(_preloaded, ref remainingStarts);
        return new WorldChunkUpdateResult(changed, started, completed, unloaded, failures);
    }

    public bool TryGetChunk(WorldChunkCoordinate coordinate, out TChunk? chunk)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_entries.TryGetValue(coordinate, out Entry? entry) &&
            entry.State == WorldChunkLoadState.Loaded)
        {
            chunk = entry.Chunk;
            return chunk is not null;
        }
        chunk = null;
        return false;
    }

    public WorldChunkResidency GetResidency(WorldChunkCoordinate coordinate)
    {
        if (!_hasDesiredSet) return WorldChunkResidency.None;
        if (_visible.Contains(coordinate)) return WorldChunkResidency.Visible;
        if (_preloaded.Contains(coordinate)) return WorldChunkResidency.Preloaded;
        if (_retained.Contains(coordinate)) return WorldChunkResidency.Retained;
        return WorldChunkResidency.None;
    }

    public WorldChunkStreamingDiagnostics CaptureDiagnostics()
    {
        int pending = 0;
        int loading = 0;
        int loaded = 0;
        int failed = 0;
        int visible = 0;
        int preloaded = 0;
        int retained = 0;
        foreach ((WorldChunkCoordinate coordinate, Entry entry) in _entries)
        {
            switch (entry.State)
            {
                case WorldChunkLoadState.Pending: pending++; break;
                case WorldChunkLoadState.Loading: loading++; break;
                case WorldChunkLoadState.Loaded: loaded++; break;
                case WorldChunkLoadState.Failed: failed++; break;
            }
            switch (GetResidency(coordinate))
            {
                case WorldChunkResidency.Visible: visible++; break;
                case WorldChunkResidency.Preloaded: preloaded++; break;
                case WorldChunkResidency.Retained: retained++; break;
            }
        }
        return new WorldChunkStreamingDiagnostics(
            _entries.Count, pending, loading, loaded, failed,
            visible, preloaded, retained, LastViewportRevision);
    }

    /// <summary>
    /// Cancels in-flight loads and enters a non-blocking retirement state. Call
    /// <see cref="DrainRetirement"/> from the owning update thread until it returns true, then
    /// dispose the streamer and its loader. Lease disposal and events remain on the caller thread.
    /// </summary>
    public void BeginRetirement()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retiring) return;
        _retiring = true;
        _hasDesiredSet = false;
        _removeScratch.Clear();
        foreach ((WorldChunkCoordinate coordinate, Entry entry) in _entries)
        {
            if (entry.State == WorldChunkLoadState.Loading)
            {
                entry.Cancellation!.Cancel();
                continue;
            }
            if (entry.Chunk is { } chunk)
            {
                entry.Chunk = null;
                chunk.Dispose();
                ChunkUnloaded?.Invoke(new WorldChunkUnloadedEvent(coordinate));
            }
            entry.Cancellation?.Dispose();
            entry.Cancellation = null;
            _removeScratch.Add(coordinate);
        }
        for (int index = 0; index < _removeScratch.Count; index++)
            _entries.Remove(_removeScratch[index]);
    }

    /// <summary>
    /// Harvests completed retirement work without waiting. Returns true after every cancelled
    /// operation and resulting lease has been observed and released on the caller thread.
    /// </summary>
    public bool DrainRetirement()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_retiring)
            throw new InvalidOperationException("BeginRetirement must be called before draining.");
        int completed = 0;
        int failures = 0;
        int unloaded = 0;
        HarvestCompleted(ref completed, ref failures, ref unloaded);
        return _activeLoads == 0 && _entries.Count == 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Entry entry in _entries.Values) entry.Cancellation?.Cancel();
        foreach ((WorldChunkCoordinate coordinate, Entry entry) in _entries)
        {
            try
            {
                TChunk? chunk = entry.Chunk;
                if (chunk is null && entry.Task is { } task)
                {
                    try { chunk = task.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { }
                    catch { }
                }
                chunk?.Dispose();
                if (chunk is not null) ChunkUnloaded?.Invoke(new WorldChunkUnloadedEvent(coordinate));
            }
            finally
            {
                entry.Cancellation?.Dispose();
            }
        }
        _entries.Clear();
        _activeLoads = 0;
    }

    private void ReconcileDesired(ref int unloaded)
    {
        _removeScratch.Clear();
        foreach ((WorldChunkCoordinate coordinate, Entry entry) in _entries)
        {
            if (_retained.Contains(coordinate)) continue;
            if (entry.State == WorldChunkLoadState.Loading)
            {
                entry.Cancellation!.Cancel();
                continue;
            }
            if (entry.Chunk is { } chunk)
            {
                entry.Chunk = null;
                chunk.Dispose();
                unloaded++;
                ChunkUnloaded?.Invoke(new WorldChunkUnloadedEvent(coordinate));
            }
            _removeScratch.Add(coordinate);
        }
        for (int i = 0; i < _removeScratch.Count; i++) _entries.Remove(_removeScratch[i]);

        for (int y = _retained.MinY; ; y++)
        {
            for (int x = _retained.MinX; ; x++)
            {
                var coordinate = new WorldChunkCoordinate(x, y);
                _entries.TryAdd(coordinate, new Entry());
                if (x == _retained.MaxX) break;
            }
            if (y == _retained.MaxY) break;
        }
    }

    private int StartDesiredLoads(WorldChunkRange range, ref int remainingStarts)
    {
        if (_activeLoads >= Options.MaximumConcurrentLoads || remainingStarts == 0)
            return 0;
        int started = 0;
        for (int y = range.MinY; ; y++)
        {
            for (int x = range.MinX; ; x++)
            {
                var coordinate = new WorldChunkCoordinate(x, y);
                Entry entry = _entries[coordinate];
                if (entry.State == WorldChunkLoadState.Failed)
                {
                    if (Options.RetryFailedOnViewportChange &&
                        entry.FailureRevision != LastViewportRevision)
                    {
                        entry.State = WorldChunkLoadState.Pending;
                        entry.Failure = null;
                    }
                }
                if (entry.State == WorldChunkLoadState.Pending)
                {
                    StartLoad(coordinate, entry);
                    started++;
                    remainingStarts--;
                    if (_activeLoads >= Options.MaximumConcurrentLoads || remainingStarts == 0)
                        return started;
                }
                if (x == range.MaxX) break;
            }
            if (y == range.MaxY) break;
        }
        return started;
    }

    private void StartLoad(WorldChunkCoordinate coordinate, Entry entry)
    {
        var cancellation = new CancellationTokenSource();
        entry.Cancellation = cancellation;
        entry.State = WorldChunkLoadState.Loading;
        bool completedSynchronously = false;
        try
        {
            ValueTask<TChunk> operation = _loader.LoadAsync(coordinate, cancellation.Token);
            if (operation.IsCompletedSuccessfully)
            {
                TChunk chunk = operation.Result ?? throw new InvalidOperationException(
                    $"Chunk loader returned null for {coordinate}.");
                entry.Cancellation.Dispose();
                entry.Cancellation = null;
                entry.Chunk = chunk;
                entry.State = WorldChunkLoadState.Loaded;
                completedSynchronously = true;
            }
            else
            {
                entry.Task = operation.AsTask();
                _activeLoads++;
            }
        }
        catch (Exception exception)
        {
            cancellation.Dispose();
            entry.Cancellation = null;
            MarkFailed(coordinate, entry, exception);
        }
        if (completedSynchronously)
            ChunkLoaded?.Invoke(new WorldChunkLoadedEvent(coordinate));
    }

    private void HarvestCompleted(ref int completed, ref int failures, ref int unloaded)
    {
        _removeScratch.Clear();
        foreach ((WorldChunkCoordinate coordinate, Entry entry) in _entries)
        {
            Task<TChunk>? task = entry.Task;
            if (entry.State != WorldChunkLoadState.Loading || task is null || !task.IsCompleted)
                continue;
            entry.Task = null;
            entry.Cancellation!.Dispose();
            entry.Cancellation = null;
            _activeLoads--;
            TChunk chunk;
            try
            {
                chunk = task.GetAwaiter().GetResult() ?? throw new InvalidOperationException(
                    $"Chunk loader returned null for {coordinate}.");
            }
            catch (OperationCanceledException)
            {
                if (_hasDesiredSet && _retained.Contains(coordinate))
                    entry.State = WorldChunkLoadState.Pending;
                else
                    _removeScratch.Add(coordinate);
                continue;
            }
            catch (Exception exception)
            {
                failures++;
                MarkFailed(coordinate, entry, exception);
                if (!_hasDesiredSet || !_retained.Contains(coordinate))
                    _removeScratch.Add(coordinate);
                continue;
            }
            completed++;
            if (!_hasDesiredSet || !_retained.Contains(coordinate))
            {
                chunk.Dispose();
                unloaded++;
                _removeScratch.Add(coordinate);
                ChunkUnloaded?.Invoke(new WorldChunkUnloadedEvent(coordinate));
            }
            else
            {
                entry.Chunk = chunk;
                entry.State = WorldChunkLoadState.Loaded;
                ChunkLoaded?.Invoke(new WorldChunkLoadedEvent(coordinate));
            }
        }
        for (int i = 0; i < _removeScratch.Count; i++) _entries.Remove(_removeScratch[i]);
    }

    private void MarkFailed(WorldChunkCoordinate coordinate, Entry entry, Exception exception)
    {
        entry.State = WorldChunkLoadState.Failed;
        entry.Failure = exception;
        entry.FailureRevision = LastViewportRevision;
        ChunkFailed?.Invoke(new WorldChunkFailedEvent(
            coordinate,
            exception,
            LastViewportRevision));
    }

    private static void ValidateViewport(in ViewportSnapshot viewport)
    {
        if (viewport.VisibleWorldBounds.Width <= 0f || viewport.VisibleWorldBounds.Height <= 0f ||
            !float.IsFinite(viewport.Zoom) || viewport.Zoom <= 0f ||
            !float.IsFinite(viewport.ScreenSize.X) || !float.IsFinite(viewport.ScreenSize.Y) ||
            viewport.ScreenSize.X <= 0f || viewport.ScreenSize.Y <= 0f)
            throw new ArgumentException("ViewportSnapshot must describe a finite positive view.", nameof(viewport));
    }

    private sealed class Entry
    {
        public WorldChunkLoadState State;
        public Task<TChunk>? Task;
        public CancellationTokenSource? Cancellation;
        public TChunk? Chunk;
        public Exception? Failure;
        public ulong FailureRevision;
    }
}
