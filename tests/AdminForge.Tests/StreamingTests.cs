using System.Runtime.CompilerServices;

namespace AdminForge.Tests;

/// <summary>Counts from the row's id, a stream of its own for every caller; id 0 fails after two values. Never registered.</summary>
public sealed class Counter
{
    public async IAsyncEnumerable<int> CountAsync(
        int from,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        for (var i = from; ; i++)
        {
            if (from == 0 && i == 2)
                throw new InvalidOperationException("sensor unplugged");
            yield return i;
            await Task.Delay(5, ct);
        }
    }
}

/// <summary>A stream is enumerated per subscriber inside an operation of its own, authorized when it starts.</summary>
public class StreamingTests
{
    private static Task<PanelHost> StartAsync() =>
        PanelHost.StartAsync(p =>
            p.Table<Ticket>(t =>
                    t.Live<Counter, int>(
                            "Count",
                            (c, row, ct) => c.CountAsync(row.Id * 100, ct),
                            w => w.Feed(n => n).Keep(5)
                        )
                        .Live<Counter, int>(
                            "Broken",
                            (c, _, ct) => c.CountAsync(0, ct),
                            w => w.Chart(n => n, n => n)
                        )
                )
                .Dashboard(
                    "ops",
                    d =>
                        d.Row(r =>
                            r.Chart<int>(
                                "Ticks",
                                c =>
                                    c.Stream<Counter>((s, ct) => s.CountAsync(1, ct))
                                        .X(n => n)
                                        .Y(n => n)
                            )
                        )
                )
        );

    [Fact]
    public async Task Each_subscriber_gets_its_own_stream_of_the_row_and_a_refused_one_gets_none()
    {
        await using var host = await StartAsync();
        var key = (await host.Db(Seed.OpenAndDoneAsync))[0].Id.ToString();
        using var a = host.As("a");
        using var b = host.As("b");

        // Both start at the row's first value: neither shares an enumeration with the other.
        var both = await Task.WhenAll(
            a.Panel.StreamLiveAsync("ticket", key, "count").Take(3).ToListAsync().AsTask(),
            b.Panel.StreamLiveAsync("ticket", key, "count").Take(3).ToListAsync().AsTask()
        );
        Assert.All(both, values => Assert.Equal([100, 101, 102], values.Select(v => (int)v[0]!)));
        Assert.Equal(
            [1, 2],
            (await a.Panel.StreamChartAsync("ops", "ticks").Take(2).ToListAsync()).Select(p =>
                (int)p[0]!
            )
        );

        // A stream is a read of the row and of the dashboard, asked when it starts.
        host.Policy.Allows = r =>
            r.User.Identity!.Name != "b"
            || r is not (TableRequest<Ticket> { Row: not null } or DashboardRequest);
        await Assert.ThrowsAsync<AdminForbiddenException>(() =>
            b.Panel.StreamLiveAsync("ticket", key, "count").FirstAsync().AsTask()
        );
        await Assert.ThrowsAsync<AdminForbiddenException>(() =>
            b.Panel.StreamChartAsync("ops", "ticks").FirstAsync().AsTask()
        );

        // A failing stream ends with a message for the widget; the cause goes to the log.
        var broken = new List<IReadOnlyList<object?>>();
        var stopped = await Assert.ThrowsAsync<AdminRejection>(async () =>
        {
            await foreach (var point in a.Panel.StreamLiveAsync("ticket", key, "broken"))
                broken.Add(point);
        });
        Assert.Equal(2, broken.Count);
        Assert.DoesNotContain("sensor", stopped.Message);
        Assert.Contains(LogCapture.Errors, e => e.Contains("sensor unplugged"));
    }
}
