namespace AdminForge.Core.LiveUpdates;

/// <summary>
/// In-process, reference-counted, multicast live data source. Each
/// <see cref="Subscribe"/> call returns an independent <see cref="IAsyncEnumerable{T}"/>
/// that yields the shared broadcast stream until the supplied
/// <see cref="CancellationToken"/> is cancelled (the enumerable terminates).
/// </summary>
/// <remarks>
/// Concrete implementations (polling/streaming) start their underlying fetch loop on
/// the first subscriber and stop when the subscriber count returns to zero. Disposing
/// the source completes all active subscribers' streams and tears down the loop.
/// </remarks>
public interface ILiveDataSource<T> : IAsyncDisposable
{
    /// <summary>
    /// Subscribe to the broadcast stream. Yields <see cref="LiveUpdate{T}"/> envelopes
    /// until <paramref name="cancellationToken"/> is cancelled or the source is disposed.
    /// </summary>
    IAsyncEnumerable<LiveUpdate<T>> Subscribe(CancellationToken cancellationToken = default);
}
