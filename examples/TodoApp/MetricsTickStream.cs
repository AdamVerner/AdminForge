using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace TodoApp;

/// <summary>One streaming sample point used by the operations dashboard.</summary>
public sealed record MetricsTick(DateTimeOffset At, double Value);

/// <summary>
/// Singleton fan-out for synthetic metrics ticks. The background service writes,
/// AdminForge subscribes to <see cref="Reader"/>.
/// </summary>
public sealed class MetricsTickStream
{
    private readonly Channel<MetricsTick> _channel = Channel.CreateBounded<MetricsTick>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        }
    );

    public ChannelWriter<MetricsTick> Writer => _channel.Writer;

    public IAsyncEnumerable<MetricsTick> Reader => _channel.Reader.ReadAllAsync();
}

/// <summary>
/// Emits one synthetic metric every 2s — a sine-modulated random walk. Wired into the
/// operations dashboard via <see cref="MetricsTickStream"/>.
/// </summary>
public sealed class MetricsBackgroundService(MetricsTickStream stream) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rand = new Random();
        var phase = 0.0;
        while (!stoppingToken.IsCancellationRequested)
        {
            phase += 0.4;
            var value = 50 + 30 * Math.Sin(phase) + (rand.NextDouble() - 0.5) * 10;
            await stream.Writer.WriteAsync(
                new MetricsTick(DateTimeOffset.UtcNow, value),
                stoppingToken
            );
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) { }
        }
    }
}
