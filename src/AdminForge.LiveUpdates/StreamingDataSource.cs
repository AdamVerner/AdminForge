using AdminForge.Core.LiveUpdates;

namespace AdminForge.LiveUpdates;

/// <summary>
/// Adapts an <see cref="IAsyncEnumerable{T}"/> into an <see cref="ILiveDataSource{T}"/>.
/// The underlying enumerator is opened once on the first subscriber and disposed when
/// the last subscriber drops. Each yielded item is multicast as a single-element
/// <see cref="LiveUpdateKind.Append"/> update.
/// </summary>
public class StreamingDataSource<T> : BroadcastDataSourceBase<T>
{
    private readonly IAsyncEnumerable<T> _stream;
    private readonly TimeProvider _timeProvider;

    public StreamingDataSource(IAsyncEnumerable<T> stream, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                var item in _stream.WithCancellation(cancellationToken).ConfigureAwait(false)
            )
            {
                Publish(LiveUpdate<T>.Append(new[] { item }, _timeProvider.GetUtcNow()));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Stream failures terminate the source's loop until a new subscriber kicks it.
            // In-process only — no resubscribe semantics yet.
        }
    }
}
