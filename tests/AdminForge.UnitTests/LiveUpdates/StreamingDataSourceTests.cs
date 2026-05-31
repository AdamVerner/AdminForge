using System.Threading.Channels;
using AdminForge.Core.LiveUpdates;
using AdminForge.LiveUpdates;

namespace AdminForge.UnitTests.LiveUpdates;

public class StreamingDataSourceTests
{
    [Fact]
    public async Task Multicasts_Each_Item_As_Append_To_All_Subscribers()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var source = new StreamingDataSource<int>(channel.Reader.ReadAllAsync());

        using var cts = new CancellationTokenSource();
        var a = new List<int>();
        var b = new List<int>();
        var aTask = Task.Run(async () =>
        {
            await foreach (var u in source.Subscribe(cts.Token))
            {
                Assert.Equal(LiveUpdateKind.Append, u.Kind);
                a.AddRange(u.Items);
                if (a.Count >= 3)
                    break;
            }
        });
        var bTask = Task.Run(async () =>
        {
            await foreach (var u in source.Subscribe(cts.Token))
            {
                Assert.Equal(LiveUpdateKind.Append, u.Kind);
                b.AddRange(u.Items);
                if (b.Count >= 3)
                    break;
            }
        });

        // Wait for both subscribers to register before pushing — otherwise the first
        // item could be consumed by A before B subscribes.
        await Wait.Until(() => source.SubscriberCount == 2);

        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);
        channel.Writer.TryWrite(3);

        await Wait.Until(() => a.Count >= 3 && b.Count >= 3);
        cts.Cancel();
        try
        {
            await Task.WhenAll(aTask, bTask);
        }
        catch (OperationCanceledException) { }

        Assert.Equal(new[] { 1, 2, 3 }, a);
        Assert.Equal(new[] { 1, 2, 3 }, b);
    }

    [Fact]
    public async Task Enumerator_Stops_When_Last_Subscriber_Drops()
    {
        var channel = Channel.CreateUnbounded<int>();
        var disposed = false;
        async IAsyncEnumerable<int> Stream(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
        )
        {
            try
            {
                await foreach (var i in channel.Reader.ReadAllAsync(ct))
                {
                    yield return i;
                }
            }
            finally
            {
                disposed = true;
            }
        }

        await using var source = new StreamingDataSource<int>(Stream());
        using var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            await foreach (var u in source.Subscribe(cts.Token))
            {
                _ = u;
                break;
            }
        });
        await Wait.Until(() => source.SubscriberCount >= 1);
        channel.Writer.TryWrite(42);
        await Wait.Until(() => !source.IsRunning, timeout: TimeSpan.FromSeconds(5));
        cts.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException) { }
        // Closing the producer channel guarantees the underlying enumerator's finally
        // runs even if cancellation alone didn't trigger it (the iterator awaits ReadAllAsync).
        channel.Writer.TryComplete();
        await Wait.Until(() => disposed, timeout: TimeSpan.FromSeconds(5));
        Assert.True(disposed);
    }

    [Fact]
    public async Task Slow_Subscriber_Drops_Oldest_And_Stream_Continues()
    {
        var channel = Channel.CreateUnbounded<int>();
        await using var source = new SmallCapacityStreamingSource<int>(
            channel.Reader.ReadAllAsync()
        );

        using var cts = new CancellationTokenSource();
        IAsyncEnumerable<LiveUpdate<int>> slow = source.Subscribe(cts.Token);
        var slowEnumerator = slow.GetAsyncEnumerator(cts.Token);

        // Kick off MoveNextAsync so the iterator body runs and registers the subscriber.
        // The very first item received is the one that satisfies SubscriberCount==1; we
        // hold it aside and continue producing to exercise drop-oldest backpressure.
        var firstMove = slowEnumerator.MoveNextAsync();
        await Wait.Until(() => source.SubscriberCount == 1, timeout: TimeSpan.FromSeconds(5));

        for (var i = 0; i < 50; i++)
            channel.Writer.TryWrite(i);
        channel.Writer.TryComplete();

        // Drain everything the subscriber buffered. With capacity 4 + DropOldest the
        // subscriber must NOT see all 50 items — it should hold at most ~4 of the most
        // recent ones (the producer is unaffected by slow consumers).
        var collected = new List<int>();
        if (await firstMove)
            collected.AddRange(slowEnumerator.Current.Items);
        while (await slowEnumerator.MoveNextAsync())
            collected.AddRange(slowEnumerator.Current.Items);
        cts.Cancel();
        try
        {
            await slowEnumerator.DisposeAsync();
        }
        catch { }

        Assert.NotEmpty(collected);
        // Producer wasn't stalled, so the channel's drop-oldest policy kicked in: we should
        // have far fewer than 50 items, and the newest (49) must be among them.
        Assert.True(
            collected.Count < 50,
            $"expected drop-oldest backpressure, got {collected.Count} items"
        );
        Assert.Contains(49, collected);
    }

    private sealed class SmallCapacityStreamingSource<T> : StreamingDataSource<T>
    {
        public SmallCapacityStreamingSource(IAsyncEnumerable<T> stream)
            : base(stream) { }

        protected override int SubscriberChannelCapacity => 4;
    }
}
