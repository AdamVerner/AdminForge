using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>A page of widgets in rows, under <c>/admin/dashboards/{id}</c>, or the home page.</summary>
public sealed class DashboardBuilder
{
    private readonly string _id;
    private readonly List<RowBuilder> _rows = [];
    private NavModel _nav;
    private string _title;
    private bool _home;

    internal DashboardBuilder(string id, NavModel nav)
    {
        _id = id;
        _nav = nav;
        _title = Members.Humanize(id.Replace('-', ' '));
    }

    public DashboardBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>The home page shows this dashboard; it leaves the sidebar.</summary>
    public DashboardBuilder Home()
    {
        _home = true;
        return this;
    }

    public DashboardBuilder Area(NavArea area)
    {
        _nav = _nav with { Area = area };
        return this;
    }

    /// <summary>A row; its widgets share the width equally.</summary>
    public DashboardBuilder Row(Action<RowBuilder> configure)
    {
        var row = new RowBuilder(_id);
        configure(row);
        _rows.Add(row);
        return this;
    }

    internal DashboardModel Build(BuildContext ctx)
    {
        var rows = _rows.Select(r => r.Build(ctx)).ToList();
        var duplicate = rows.SelectMany(r => r)
            .GroupBy(w => w.Id)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Dashboard '{_id}' has two widgets titled so their id is '{duplicate.Key}'."
            );
        return new()
        {
            Id = _id,
            Title = _title,
            Nav = _home ? _nav with { Hidden = true } : _nav,
            Home = _home,
            Rows = rows,
        };
    }
}

/// <summary>One row of a dashboard; its widgets share the width equally.</summary>
public sealed class RowBuilder
{
    private readonly string _dashboard;
    private readonly List<Func<BuildContext, WidgetModel>> _widgets = [];

    internal RowBuilder(string dashboard) => _dashboard = dashboard;

    /// <summary>One value from <typeparamref name="TService"/>, fetched when the dashboard loads.</summary>
    public RowBuilder Stat<TService, TValue>(
        string title,
        Func<TService, CancellationToken, Task<TValue>> value,
        string? suffix = null
    )
        where TService : notnull
    {
        _widgets.Add(_ => new StatModel
        {
            Id = Members.Kebab(title),
            Title = title,
            Suffix = suffix,
            Fetch = async (sp, ct) =>
                await value(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ct)
                    .ConfigureAwait(false),
        });
        return this;
    }

    /// <summary>A line chart of points loaded once, polled, or streamed.</summary>
    public RowBuilder Chart<TPoint>(string title, Action<ChartBuilder<TPoint>> configure)
        where TPoint : notnull
    {
        var chart = new ChartBuilder<TPoint>();
        configure(chart);
        _widgets.Add(_ => chart.Build(title, _dashboard));
        return this;
    }

    /// <summary>The first rows of <typeparamref name="T"/>'s table, read as that table's list is.</summary>
    public RowBuilder Table<T>(string title, Action<TableWidgetBuilder<T>>? configure = null)
        where T : class
    {
        var table = new TableWidgetBuilder<T>();
        configure?.Invoke(table);
        _widgets.Add(ctx => table.Build(ctx, title, _dashboard));
        return this;
    }

    internal IReadOnlyList<WidgetModel> Build(BuildContext ctx) =>
        _widgets.Select(w => w(ctx)).ToList();
}

/// <summary>A line chart: where its points come from and which properties are X and Y.</summary>
public sealed class ChartBuilder<TPoint>
    where TPoint : notnull
{
    private Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<object>>>? _points;
    private Func<IServiceProvider, CancellationToken, IAsyncEnumerable<object>>? _stream;
    private Func<object, object?>? _x;
    private Func<object, object?>? _y;
    private string? _yLabel;
    private TimeSpan? _poll;
    private int _window = 50;

    public ChartBuilder<TPoint> Points<TService>(
        Func<TService, CancellationToken, Task<IReadOnlyList<TPoint>>> points
    )
        where TService : notnull
    {
        _points = async (sp, ct) =>
            (
                await points(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ct)
                    .ConfigureAwait(false)
            )
                .Cast<object>()
                .ToList();
        return this;
    }

    /// <summary>Points pushed by <typeparamref name="TService"/>; each viewer enumerates the stream on its own.</summary>
    public ChartBuilder<TPoint> Stream<TService>(
        Func<TService, CancellationToken, IAsyncEnumerable<TPoint>> stream
    )
        where TService : notnull
    {
        _stream = (sp, ct) =>
            stream(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), ct)
                .Select(p => (object)p);
        return this;
    }

    public ChartBuilder<TPoint> X(Expression<Func<TPoint, object?>> x)
    {
        var read = x.Compile();
        _x = p => read((TPoint)p);
        return this;
    }

    public ChartBuilder<TPoint> Y(Expression<Func<TPoint, object?>> y, string? label = null)
    {
        var read = y.Compile();
        _y = p => read((TPoint)p);
        _yLabel = label;
        return this;
    }

    /// <summary>Loads the points again every <paramref name="interval"/>.</summary>
    public ChartBuilder<TPoint> Poll(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _poll = interval;
        return this;
    }

    /// <summary>The points a live chart keeps.</summary>
    public ChartBuilder<TPoint> Window(int points)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(points, 0);
        _window = points;
        return this;
    }

    internal ChartModel Build(string title, string dashboard)
    {
        var who = $"Chart '{title}' on dashboard '{dashboard}'";
        if (_points is null == (_stream is null))
            throw new InvalidOperationException(
                $"{who} needs Points(...) or Stream(...), not both."
            );
        if (_poll is not null && _points is null)
            throw new InvalidOperationException($"{who} polls, which needs Points(...).");
        return new()
        {
            Id = Members.Kebab(title),
            Title = title,
            Points = _points,
            Stream = _stream,
            X = _x ?? throw new InvalidOperationException($"{who} needs X(...)."),
            Y = _y ?? throw new InvalidOperationException($"{who} needs Y(...)."),
            YLabel = _yLabel,
            Poll = _poll,
            Window = _window,
        };
    }
}

/// <summary>A table widget: columns, sort and row count.</summary>
public sealed class TableWidgetBuilder<T>
    where T : class
{
    private List<string>? _columns;
    private string? _sort;
    private bool _descending;
    private int _take = 10;
    private string? _table;

    public TableWidgetBuilder<T> Columns(params Expression<Func<T, object?>>[] columns)
    {
        _columns = columns.Select(Members.Name).ToList();
        return this;
    }

    public TableWidgetBuilder<T> Sort<TProp>(
        Expression<Func<T, TProp>> column,
        bool descending = false
    )
    {
        _sort = Members.Name(column);
        _descending = descending;
        return this;
    }

    public TableWidgetBuilder<T> Take(int rows)
    {
        _take = rows;
        return this;
    }

    /// <summary>The table, when <typeparamref name="T"/> has more than one.</summary>
    public TableWidgetBuilder<T> Table(string tableId)
    {
        _table = tableId;
        return this;
    }

    internal TableWidgetModel Build(BuildContext ctx, string title, string dashboard) =>
        new()
        {
            Id = Members.Kebab(title),
            Title = title,
            TableId = ctx.TableFor(
                typeof(T),
                _table,
                $"Table widget '{title}' on dashboard '{dashboard}'"
            ),
            Columns = _columns,
            SortBy = _sort,
            Descending = _descending,
            Take = _take,
        };
}

/// <summary>A host page's title and place in the sidebar.</summary>
public sealed class PageBuilder
{
    private readonly Type _component;
    private readonly string _id;
    private NavModel _nav;
    private string _title;

    internal PageBuilder(Type component, NavModel nav)
    {
        _component = component;
        _nav = nav;
        var name = component.Name.EndsWith("Page", StringComparison.Ordinal)
            ? component.Name[..^4]
            : component.Name;
        _id = Members.Kebab(name);
        _title = Members.Humanize(name);
    }

    public PageBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    public PageBuilder Area(NavArea area)
    {
        _nav = _nav with { Area = area };
        return this;
    }

    internal PageModel Build()
    {
        var routes = _component
            .GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .ToList();
        var route =
            routes.FirstOrDefault(r =>
                r.StartsWith(PanelModel.Root + "/", StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                routes.Count == 0
                    ? $"Page<{_component.Name}> is not routable. Add @page \"{PanelModel.Root}/...\" to it."
                    : $"Page<{_component.Name}>: its route '{routes[0]}' is outside the panel. Route it under '{PanelModel.Root}/'."
            );
        return new PageModel(_id, _component, _title, route, _nav);
    }
}
