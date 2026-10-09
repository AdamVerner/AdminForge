using System.Runtime.CompilerServices;

namespace TodoApp;

/// <summary>One sample of a synthetic metric.</summary>
public sealed record MetricsTick(DateTimeOffset At, double Value);

/// <summary>A synthetic metric, a sine-modulated random walk; every call is a stream of its own.</summary>
public sealed class MetricsTickStream
{
    public async IAsyncEnumerable<MetricsTick> Ticks(
        int seed,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var rand = new Random(seed);
        var phase = 0.0;
        while (!ct.IsCancellationRequested)
        {
            phase += 0.4;
            yield return new MetricsTick(
                DateTimeOffset.UtcNow,
                50 + 30 * Math.Sin(phase) + (rand.NextDouble() - 0.5) * 10
            );
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
