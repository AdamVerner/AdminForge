using System.Linq.Expressions;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent composer for a single dashboard. Mirrors the sketch in the project
/// context: widgets are added in registration order and arranged into a row-based
/// layout via <see cref="Layout"/>.
/// </summary>
public sealed class DashboardBuilder
{
    private readonly string _routeName;
    private string _title;
    private readonly NavMeta _nav = new();
    private readonly List<WidgetMeta> _widgets = [];
    private DashboardLayoutMeta? _layout;

    internal DashboardBuilder(string routeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        _routeName = routeName;
        _title = routeName;
    }

    /// <summary>Sets the human-readable title shown above the grid.</summary>
    public DashboardBuilder WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        return this;
    }

    /// <summary>Customise the sidebar nav entry (label, group, order, hidden).</summary>
    public DashboardBuilder Nav(Action<NavBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NavBuilder(_nav));
        return this;
    }

    /// <summary>
    /// Adds a single-value stat card. <paramref name="fetch"/> is invoked once per
    /// page load (Phase 3) — live updates land in Phase 5.
    /// </summary>
    public DashboardBuilder AddStatCard(
        string title,
        Func<Task<object?>> fetch,
        string? suffix = null
    )
    {
        ArgumentNullException.ThrowIfNull(fetch);
        return AddStatCard(title, (_, _) => fetch(), suffix);
    }

    /// <summary>
    /// Adds a single-value stat card with access to a scoped <see cref="IServiceProvider"/>.
    /// </summary>
    public DashboardBuilder AddStatCard(
        string title,
        Func<IServiceProvider, CancellationToken, Task<object?>> fetch,
        string? suffix = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(fetch);
        var id = AllocateId(title);
        _widgets.Add(
            new StatCardMeta
            {
                Id = id,
                Title = title,
                Fetch = fetch,
                Suffix = suffix,
            }
        );
        return this;
    }

    /// <summary>
    /// Adds a line-chart widget. The user provides a typed fetch + selectors and
    /// the builder boxes them into the POCO meta.
    /// </summary>
    public DashboardBuilder AddLineChart<TPoint>(
        string title,
        Func<Task<IReadOnlyList<TPoint>>> fetch,
        Expression<Func<TPoint, object?>> xAxis,
        Expression<Func<TPoint, object?>> yAxis,
        string? xAxisLabel = null,
        string? yAxisLabel = null
    ) => AddLineChart(title, (_, _) => fetch(), xAxis, yAxis, xAxisLabel, yAxisLabel);

    /// <summary>
    /// Adds a line-chart widget with scoped service-provider access.
    /// </summary>
    public DashboardBuilder AddLineChart<TPoint>(
        string title,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TPoint>>> fetch,
        Expression<Func<TPoint, object?>> xAxis,
        Expression<Func<TPoint, object?>> yAxis,
        string? xAxisLabel = null,
        string? yAxisLabel = null
    ) => AddLineChart(title, fetch, xAxis, yAxis, xAxisLabel, yAxisLabel, configure: null);

    /// <summary>
    /// Adds a line-chart widget with an additional configuration callback that
    /// exposes live-update opt-ins (<c>WithStreaming</c>, <c>WithLivePolling</c>).
    /// </summary>
    public DashboardBuilder AddLineChart<TPoint>(
        string title,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<TPoint>>> fetch,
        Expression<Func<TPoint, object?>> xAxis,
        Expression<Func<TPoint, object?>> yAxis,
        string? xAxisLabel,
        string? yAxisLabel,
        Action<LineChartBuilder<TPoint>>? configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(xAxis);
        ArgumentNullException.ThrowIfNull(yAxis);

        var compiledX = xAxis.Compile();
        var compiledY = yAxis.Compile();

        var id = AllocateId(title);
        var meta = new LineChartMeta
        {
            Id = id,
            Title = title,
            Fetch = async (sp, ct) =>
            {
                var typed = await fetch(sp, ct).ConfigureAwait(false);
                var boxed = new List<object>(typed.Count);
                foreach (var p in typed)
                {
                    if (p is null)
                        continue;
                    boxed.Add(p);
                }
                return boxed;
            },
            XSelector = o => compiledX((TPoint)o),
            YSelector = o => compiledY((TPoint)o),
            XAxisLabel = xAxisLabel,
            YAxisLabel = yAxisLabel,
        };
        configure?.Invoke(new LineChartBuilder<TPoint>(meta));
        _widgets.Add(meta);
        return this;
    }

    /// <summary>
    /// Adds a line chart with no initial-load fetch — useful for streaming-only charts
    /// where the points are pushed exclusively via <see cref="LineChartBuilder{TPoint}.WithStreaming"/>.
    /// </summary>
    public DashboardBuilder AddLineChart<TPoint>(
        string title,
        Expression<Func<TPoint, object?>> xAxis,
        Expression<Func<TPoint, object?>> yAxis,
        Action<LineChartBuilder<TPoint>> configure,
        string? xAxisLabel = null,
        string? yAxisLabel = null
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddLineChart<TPoint>(
            title,
            (_, _) => Task.FromResult<IReadOnlyList<TPoint>>(Array.Empty<TPoint>()),
            xAxis,
            yAxis,
            xAxisLabel,
            yAxisLabel,
            configure
        );
    }

    /// <summary>
    /// Adds a read-only table widget showing rows of the supplied entity type.
    /// The entity must be registered with <c>AddTable&lt;T&gt;</c> on the parent builder.
    /// </summary>
    public DashboardBuilder AddTable<TEntity>(Action<TableWidgetBuilder<TEntity>>? configure = null)
        where TEntity : class
    {
        var sub = new TableWidgetBuilder<TEntity>();
        configure?.Invoke(sub);
        var meta = sub.Build(AllocateId(sub.Title ?? typeof(TEntity).Name));
        _widgets.Add(meta);
        return this;
    }

    /// <summary>
    /// Defines the row-based layout. If omitted, the renderer falls back to one
    /// widget per full-width row in registration order.
    /// </summary>
    public DashboardBuilder Layout(Action<LayoutBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var lb = new LayoutBuilder(_widgets);
        configure(lb);
        _layout = lb.Build();
        return this;
    }

    internal DashboardMeta Build() =>
        new()
        {
            RouteName = _routeName,
            Title = _title,
            Nav = _nav,
            Widgets = _widgets.AsReadOnly(),
            Layout = _layout,
        };

    private string AllocateId(string seed)
    {
        // Stable, slugified id; suffix with index when collisions occur.
        var slug = Slugify(seed);
        if (string.IsNullOrEmpty(slug))
            slug = "widget";
        var candidate = slug;
        var n = 2;
        while (_widgets.Any(w => string.Equals(w.Id, candidate, StringComparison.Ordinal)))
        {
            candidate = $"{slug}-{n++}";
        }
        return candidate;
    }

    private static string Slugify(string input)
    {
        var chars = input
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var s = new string(chars);
        while (s.Contains("--"))
            s = s.Replace("--", "-");
        return s.Trim('-');
    }
}
