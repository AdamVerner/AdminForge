using AdminForge.Core.LiveUpdates;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface attached to the <see cref="LineChartMeta"/> produced inside
/// <see cref="DashboardBuilder.AddLineChart{TPoint}"/>. Exposes the live-update
/// opt-in methods; live wiring is consumed by the renderer at page-mount time.
/// </summary>
public sealed class LineChartBuilder<TPoint>
{
    private readonly LineChartMeta _meta;

    internal LineChartBuilder(LineChartMeta meta)
    {
        _meta = meta;
    }

    /// <summary>
    /// Trim the live window to <paramref name="count"/> points (default 50).
    /// </summary>
    public LineChartBuilder<TPoint> WithWindowSize(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(count),
                count,
                "Window size must be positive."
            );
        _meta.LiveWindowSize = count;
        return this;
    }

    /// <summary>
    /// Drive the chart from an <see cref="IAsyncEnumerable{TPoint}"/>. Each yielded
    /// item becomes an append-style update; the chart trims to the configured window.
    /// </summary>
    public LineChartBuilder<TPoint> WithStreaming(IAsyncEnumerable<TPoint> stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        EnsureNoExistingLiveSource();
        _meta.LiveDataSource = new LiveDataSourceMeta
        {
            ItemType = typeof(TPoint),
            Payload = stream,
        };
        return this;
    }

    /// <summary>
    /// Periodically re-invoke the chart's <em>existing</em> data delegate (registered via
    /// <c>AddLineChart</c>) every <paramref name="interval"/>. No fetch delegate is taken
    /// here — the chart page reuses the dashboard widget materialiser to refresh the
    /// displayed points.
    /// </summary>
    public LineChartBuilder<TPoint> WithLivePolling(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "Interval must be positive."
            );
        EnsureNoExistingLiveSource();
        _meta.LivePollingInterval = interval;
        return this;
    }

    private void EnsureNoExistingLiveSource()
    {
        if (_meta.LiveDataSource is not null || _meta.LivePollingInterval is not null)
        {
            throw new InvalidOperationException(
                $"Line chart '{_meta.Title}' already has a live data source configured. "
                    + "WithLivePolling/WithStreaming may only be called once."
            );
        }
    }
}
