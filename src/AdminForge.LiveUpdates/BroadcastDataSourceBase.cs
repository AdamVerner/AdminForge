using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AdminForge.Core.LiveUpdates;

namespace AdminForge.LiveUpdates;

/// <summary>
/// Shared scaffolding for <see cref="ILiveDataSource{T}"/> implementations: reference-counted
/// fanout, per-subscriber bounded channel (drop-oldest backpressure), and cooperative teardown
/// when the last subscriber drops or the source is disposed.
/// </summary>
/// <remarks>
/// Concrete subclasses override <see cref="StartCoreAsync"/> (called once on the first subscriber)
/// and <see cref="StopCoreAsync"/> (called when the last subscriber disconnects). They emit
/// updates via <see cref="Publish"/>.
/// </remarks>
public abstract class BroadcastDataSourceBase<T> : ILiveDataSource<T>
{
    private readonly object _gate = new();
    private readonly List<Subscriber> _subscribers = [];
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _disposed;

    /// <summary>Channel capacity per subscriber. Drop-oldest under backpressure.</summary>
    protected virtual int SubscriberChannelCapacity => 256;

    /// <summary>The most recent <see cref="LiveUpdateKind.FullReplace"/> snapshot, if any.</summary>
    protected LiveUpdate<T>? LastSnapshot { get; private set; }

    public IAsyncEnumerable<LiveUpdate<T>> Subscribe(
        CancellationToken cancellationToken = default
    ) => SubscribeCore(cancellationToken);

    private async IAsyncEnumerable<LiveUpdate<T>> SubscribeCore(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var channel = Channel.CreateBounded<LiveUpdate<T>>(
            new BoundedChannelOptions(SubscriberChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            }
        );
        var subscriber = new Subscriber(channel);

        LiveUpdate<T>? replay;
        bool startNow;
        lock (_gate)
        {
            _subscribers.Add(subscriber);
            startNow = _subscribers.Count == 1;
            replay = LastSnapshot;
        }

        if (startNow)
        {
            // First subscriber — kick off the underlying loop.
            _loopCts = new CancellationTokenSource();
            var ct = _loopCts.Token;
            _loopTask = Task.Run(async () =>
            {
                try
                {
                    await StartCoreAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    // Loop ended (naturally or via cancellation). Signal end-of-stream to
                    // all current subscribers so their MoveNextAsync returns false instead
                    // of hanging. TryComplete is idempotent against UnsubscribeAsync.
                    Subscriber[] toFinish;
                    lock (_gate)
                        toFinish = _subscribers.ToArray();
                    foreach (var s in toFinish)
                        s.Channel.Writer.TryComplete();
                }
            });
        }

        // Replay the latest snapshot so a newly connected tab renders immediately
        // instead of waiting for the next tick.
        if (replay is not null)
        {
            // Best-effort write; if the channel can't accept, drop-oldest will handle it.
            await channel.Writer.WriteAsync(replay, CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            await foreach (
                var update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                yield return update;
            }
        }
        finally
        {
            await UnsubscribeAsync(subscriber).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes an update to all currently connected subscribers and (if it is a
    /// <see cref="LiveUpdateKind.FullReplace"/>) records the snapshot for replay.
    /// </summary>
    protected void Publish(LiveUpdate<T> update)
    {
        Subscriber[] snapshot;
        lock (_gate)
        {
            if (update.Kind == LiveUpdateKind.FullReplace)
                LastSnapshot = update;
            snapshot = _subscribers.ToArray();
        }
        foreach (var s in snapshot)
        {
            // BoundedChannelFullMode.DropOldest guarantees TryWrite always succeeds (until completed).
            s.Channel.Writer.TryWrite(update);
        }
    }

    private async Task UnsubscribeAsync(Subscriber subscriber)
    {
        bool stopNow;
        CancellationTokenSource? toCancel = null;
        Task? toAwait = null;
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
            stopNow = _subscribers.Count == 0 && !_disposed;
            if (stopNow)
            {
                toCancel = _loopCts;
                toAwait = _loopTask;
                _loopCts = null;
                _loopTask = null;
            }
        }
        subscriber.Channel.Writer.TryComplete();
        if (stopNow)
        {
            toCancel?.Cancel();
            try
            {
                if (toAwait is not null)
                    await toAwait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            await StopCoreAsync().ConfigureAwait(false);
            toCancel?.Dispose();
        }
    }

    /// <summary>
    /// Run the underlying fetch loop until <paramref name="cancellationToken"/> fires.
    /// Called exactly once per active-subscriber generation (first subscriber).
    /// </summary>
    protected abstract Task StartCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tear down any side state (timers, enumerators) after <see cref="StartCoreAsync"/>
    /// has completed. Called after the last subscriber disconnects.
    /// </summary>
    protected virtual Task StopCoreAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? toCancel;
        Task? toAwait;
        Subscriber[] snapshot;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            toCancel = _loopCts;
            toAwait = _loopTask;
            _loopCts = null;
            _loopTask = null;
            snapshot = _subscribers.ToArray();
            _subscribers.Clear();
        }
        foreach (var s in snapshot)
            s.Channel.Writer.TryComplete();
        toCancel?.Cancel();
        try
        {
            if (toAwait is not null)
                await toAwait.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        await StopCoreAsync().ConfigureAwait(false);
        toCancel?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Test/diagnostic hook — number of currently connected subscribers.</summary>
    public int SubscriberCount
    {
        get
        {
            lock (_gate)
                return _subscribers.Count;
        }
    }

    /// <summary>Test/diagnostic hook — true while the underlying loop is running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _loopTask is not null;
        }
    }

    private sealed record Subscriber(Channel<LiveUpdate<T>> Channel);
}
