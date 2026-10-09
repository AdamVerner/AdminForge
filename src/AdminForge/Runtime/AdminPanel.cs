using System.Globalization;
using System.Reflection;

namespace AdminForge;

/// <summary>The panel's one runtime surface, addressed by stable ids; its views never carry the row itself.</summary>
internal sealed class AdminPanel(PanelModel model, OperationRunner runner)
{
    private const int MaxPageSize = 500;

    public PanelModel Model => model;

    /// <summary>Recorded on audit events; MCP sets it for the calls it makes.</summary>
    public AdminSurface Surface { get; set; } = AdminSurface.Panel;

    public Task<ListView> ListAsync(
        string tableId,
        ListRequest request,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Listing {tableId}",
            async op =>
            {
                var t = Table(tableId);
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Read, null), $"read {t.Title}");
                var page = Math.Max(0, request.Page);
                var size = Math.Clamp(request.PageSize, 1, MaxPageSize);
                var (rows, total) = await QueryAsync(
                    op,
                    t,
                    request with
                    {
                        Page = page,
                        PageSize = size,
                    }
                );
                var views = new List<RowView>(rows.Count);
                var labels = new Dictionary<(string, string), string?>();
                foreach (var row in rows)
                    views.Add(await ViewAsync(op, t, row, listed: true, labels));
                var canCreate =
                    t.Source.Create is not null && await op.AllowsAsync(t, AdminOp.Create, null);
                return new ListView(t.Id, views, total, page, size, canCreate);
            },
            ct
        );

    /// <summary>Null when no row has the key.</summary>
    public Task<DetailView?> GetAsync(string tableId, string key, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Reading {tableId}",
            async op =>
            {
                var t = Table(tableId);
                var row = await FindAsync(op, t, key);
                if (row is null)
                    return null;
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Read, row), $"read {t.Title}");
                var related = t
                    .Related.Select(r => new RelatedView(
                        r,
                        new Dictionary<string, object?> { [r.ForeignKey] = t.Key.Values(row)[0] }
                    ))
                    .ToList();
                return (DetailView?)
                    new DetailView(await ViewAsync(op, t, row, listed: false, []), related);
            },
            ct
        );

    /// <summary>The create form: the editable columns in the table's order, with the row type's defaults.</summary>
    public Task<FormView> NewAsync(string tableId, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Creating in {tableId}",
            async op =>
            {
                var t = Table(tableId);
                if (t.Source.Create is null)
                    throw new AdminRejection($"{t.Title} cannot be created in the panel.");
                await op.EnsureAsync(
                    op.AllowsAsync(t, AdminOp.Create, null),
                    $"create in {t.Title}"
                );
                var fields = Fields(t, creating: true);
                var defaults = t.Rows.Defaults();
                return await FormAsync(op, fields, f => defaults.GetValueOrDefault(f.Name));
            },
            ct
        );

    /// <summary>The edit form with the row's values; null when no row has the key.</summary>
    public Task<FormView?> EditAsync(string tableId, string key, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Editing {tableId}",
            async op =>
            {
                var t = Table(tableId);
                if (t.Source.Update is null)
                    throw new AdminRejection($"{t.Title} cannot be edited in the panel.");
                var row = await FindAsync(op, t, key);
                if (row is null)
                    return null;
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Read, row), $"read {t.Title}");
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Update, row), $"edit {t.Title}");
                return (FormView?)
                    await FormAsync(
                        op,
                        Fields(t, creating: false),
                        f => t.Column(f.Name)!.Read(row)
                    );
            },
            ct
        );

    /// <summary>Returns the new row's key.</summary>
    public Task<string> CreateAsync(
        string tableId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Creating in {tableId}",
            async op =>
            {
                var t = Table(tableId);
                var create =
                    t.Source.Create
                    ?? throw new AdminRejection($"{t.Title} cannot be created in the panel.");
                await op.EnsureAsync(
                    op.AllowsAsync(t, AdminOp.Create, null),
                    $"create in {t.Title}"
                );
                var valid = await Validator.CheckAsync(
                    Fields(t, creating: true),
                    values,
                    partial: false,
                    op
                );
                var created = await create(op.Services, t.Rows.Create(valid), op.CancellationToken);
                var key = t.Key.Encode(created);
                await op.AuditAsync(
                    Surface,
                    AdminAuditOperation.Create,
                    t.Id,
                    key,
                    Snapshot(created)
                        .ToDictionary(kv => kv.Key, kv => new AdminChange(null, kv.Value))
                );
                return key;
            },
            ct
        );

    /// <summary>Writes the submitted columns only; the source receives the names of the ones that changed.</summary>
    public Task UpdateAsync(
        string tableId,
        string key,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Saving {tableId}",
            async op =>
            {
                var t = Table(tableId);
                var update =
                    t.Source.Update
                    ?? throw new AdminRejection($"{t.Title} cannot be edited in the panel.");
                var row = await RequireAsync(op, t, key);
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Update, row), $"edit {t.Title}");
                var valid = await Validator.CheckAsync(
                    Fields(t, creating: false),
                    values,
                    partial: true,
                    op
                );
                var changes = valid
                    .Where(kv => !Equals(t.Column(kv.Key)!.Read(row), kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (changes.Count == 0)
                    return true;
                var before = Snapshot(row);
                var patched = t.Rows.Copy(row, changes);
                var stored = await update(
                    op.Services,
                    patched,
                    changes.Keys.ToHashSet(),
                    op.CancellationToken
                );
                var after = Snapshot(stored);
                await op.AuditAsync(
                    Surface,
                    AdminAuditOperation.Update,
                    t.Id,
                    key,
                    after
                        .Where(kv => !Equals(before.GetValueOrDefault(kv.Key), kv.Value))
                        .ToDictionary(
                            kv => kv.Key,
                            kv => new AdminChange(before.GetValueOrDefault(kv.Key), kv.Value)
                        )
                );
                return true;
            },
            ct
        );

    public Task DeleteAsync(string tableId, string key, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Deleting from {tableId}",
            async op =>
            {
                var t = Table(tableId);
                var delete =
                    t.Source.Delete
                    ?? throw new AdminRejection($"{t.Title} cannot be deleted from in the panel.");
                var row = await RequireAsync(op, t, key);
                await op.EnsureAsync(
                    op.AllowsAsync(t, AdminOp.Delete, row),
                    $"delete from {t.Title}"
                );
                var before = Snapshot(row);
                await delete(op.Services, row, op.CancellationToken);
                await op.AuditAsync(
                    Surface,
                    AdminAuditOperation.Delete,
                    t.Id,
                    key,
                    before.ToDictionary(kv => kv.Key, kv => new AdminChange(kv.Value, null))
                );
                return true;
            },
            ct
        );

    /// <summary>The action's inputs. Without a key the panel only asks whether the action is offered at all.</summary>
    public Task<FormView> ActionFormAsync(
        string tableId,
        string? key,
        string actionId,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Opening {actionId}",
            async op =>
            {
                var t = Table(tableId);
                var action = Action(t, actionId);
                var row = key is null ? null : await RequireAsync(op, t, key);
                await op.EnsureAsync(op.AllowsAsync(t, action, row), $"run {action.Title}");
                if (action.Input is not { } input)
                    return new FormView(
                        [],
                        [],
                        new Dictionary<string, IReadOnlyList<AdminOption>>()
                    );
                var defaults = input.Factory.Defaults();
                return await FormAsync(op, input.Fields, f => defaults.GetValueOrDefault(f.Name));
            },
            ct
        );

    public Task<Outcome> RunActionAsync(
        string tableId,
        string key,
        string actionId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Running {actionId}",
            async op =>
            {
                var t = Table(tableId);
                var action = Action(t, actionId);
                var row = await RequireAsync(op, t, key);
                await op.EnsureAsync(op.AllowsAsync(t, action, row), $"run {action.Title}");
                var valid = await Validator.CheckAsync(
                    action.Input?.Fields ?? [],
                    values,
                    partial: false,
                    op
                );
                var input = action.Input?.Factory.Create(valid);
                var done = await action.Run(op.Services, row, input, op.User, op.CancellationToken);
                await op.AuditAsync(
                    Surface,
                    AdminAuditOperation.Action,
                    t.Id,
                    key,
                    action: action.Id,
                    input: Logged(valid)
                );
                return new Outcome(done.Message, done.Markdown);
            },
            ct
        );

    public Task<FormView> FormAsync(string formId, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Opening {formId}",
            async op =>
            {
                var form = await OpenFormAsync(op, formId);
                var defaults = form.Input.Factory.Defaults();
                return await FormAsync(
                    op,
                    form.Input.Fields,
                    f => defaults.GetValueOrDefault(f.Name)
                );
            },
            ct
        );

    public Task<Outcome> SubmitFormAsync(
        string formId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Submitting {formId}",
            async op =>
            {
                var form = await OpenFormAsync(op, formId);
                var valid = await Validator.CheckAsync(
                    form.Input.Fields,
                    values,
                    partial: false,
                    op
                );
                var done = await form.Run(
                    op.Services,
                    form.Input.Factory.Create(valid),
                    op.User,
                    op.CancellationToken
                );
                await op.AuditAsync(
                    Surface,
                    AdminAuditOperation.Form,
                    null,
                    null,
                    action: form.Id,
                    input: Logged(valid)
                );
                return new Outcome(done.Message, done.Markdown);
            },
            ct
        );

    /// <summary>Every widget's data; a widget that fails carries its error and the rest still load.</summary>
    public Task<DashboardView> DashboardAsync(string dashboardId, CancellationToken ct = default) =>
        runner.RunAsync(
            $"Loading {dashboardId}",
            async op =>
            {
                var d = await OpenDashboardAsync(op, dashboardId);
                var widgets = new Dictionary<string, WidgetView>(StringComparer.Ordinal);
                foreach (var w in d.Rows.SelectMany(r => r))
                    widgets[w.Id] = await WidgetAsync(op, w);
                return new DashboardView(d, widgets);
            },
            ct
        );

    /// <summary>One widget again, as a polling chart asks for it; authorized as the dashboard every time.</summary>
    public Task<WidgetView> WidgetAsync(
        string dashboardId,
        string widgetId,
        CancellationToken ct = default
    ) =>
        runner.RunAsync(
            $"Loading {dashboardId}",
            async op =>
            {
                var d = await OpenDashboardAsync(op, dashboardId);
                return await WidgetAsync(
                    op,
                    d.Widget(widgetId)
                        ?? throw new AdminRejection(
                            $"Dashboard '{d.Id}' has no widget '{widgetId}'."
                        )
                );
            },
            ct
        );

    /// <summary>A streamed chart's points, each as its x and y.</summary>
    public IAsyncEnumerable<IReadOnlyList<object?>> StreamChartAsync(
        string dashboardId,
        string widgetId,
        CancellationToken ct = default
    ) =>
        runner.StreamAsync(
            $"Streaming {widgetId}",
            async op =>
            {
                var d = await OpenDashboardAsync(op, dashboardId);
                var chart =
                    d.Widget(widgetId) as ChartModel
                    ?? throw new AdminRejection($"Dashboard '{d.Id}' has no chart '{widgetId}'.");
                var stream =
                    chart.Stream
                    ?? throw new AdminRejection($"Chart '{widgetId}' does not stream.");
                return stream(op.Services, op.CancellationToken)
                    .Select<object, IReadOnlyList<object?>>(p => [chart.X(p), Number(chart.Y(p))]);
            },
            ct
        );

    /// <summary>A row's live widget: authorized as a read of the row, enumerated for this caller alone.</summary>
    public IAsyncEnumerable<IReadOnlyList<object?>> StreamLiveAsync(
        string tableId,
        string key,
        string liveId,
        CancellationToken ct = default
    ) =>
        runner.StreamAsync(
            $"Streaming {liveId}",
            async op =>
            {
                var t = Table(tableId);
                var live =
                    t.Live.FirstOrDefault(l => l.Id == liveId)
                    ?? throw new AdminRejection($"Table '{t.Id}' has no live widget '{liveId}'.");
                var row = await RequireAsync(op, t, key);
                await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Read, row), $"read {t.Title}");
                return live.Stream(op.Services, row, op.CancellationToken)
                    .Select<object, IReadOnlyList<object?>>(p =>
                        live.Chart
                            ? [live.Cells[0].Read(p), Number(live.Cells[1].Read(p))]
                            : live.Cells.Select(c => c.Read(p)).ToList()
                    );
            },
            ct
        );

    /// <summary>The rows matching <paramref name="text"/> in every searchable table the caller may read.</summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string text,
        int perTable = 5,
        CancellationToken ct = default
    )
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrWhiteSpace(text))
            return hits;
        foreach (var t in model.Tables.Where(t => t.Searchable && !t.Nav.Hidden))
        {
            try
            {
                var list = await ListAsync(
                    t.Id,
                    new ListRequest(PageSize: perTable, Search: text),
                    ct
                );
                hits.AddRange(list.Rows.Select(r => new SearchHit(t.Id, t.Title, r.Key, r.Label)));
            }
            // The runner has logged a failure; one table must not empty the search.
            catch (AdminRejection) { }
        }
        return hits;
    }

    /// <summary>The entries the caller may open, in registration order.</summary>
    public Task<IReadOnlyList<NavItem>> NavAsync(CancellationToken ct = default) =>
        runner.RunAsync(
            "Listing the panel",
            async op =>
            {
                var items = new List<NavItem>();
                foreach (var t in model.Tables.Where(t => !t.Nav.Hidden))
                    if (await op.AllowsAsync(t, AdminOp.Read, null))
                        items.Add(new(t.Id, t.Title, $"{PanelModel.Root}/entities/{t.Id}", t.Nav));
                foreach (var d in model.Dashboards.Where(d => !d.Nav.Hidden))
                    if (await op.AllowsAsync(new DashboardRequest(op.User, d.Id)))
                        items.Add(
                            new(d.Id, d.Title, $"{PanelModel.Root}/dashboards/{d.Id}", d.Nav)
                        );
                foreach (var f in model.Forms.Where(f => !f.Nav.Hidden))
                    if (await op.AllowsAsync(new FormRequest(op.User, f.Id)))
                        items.Add(new(f.Id, f.Title, $"{PanelModel.Root}/forms/{f.Id}", f.Nav));
                foreach (var p in model.Pages.Where(p => !p.Nav.Hidden))
                    if (await op.AllowsAsync(new PageRequest(op.User, p.Id)))
                        items.Add(new(p.Id, p.Title, p.Route, p.Nav));
                return (IReadOnlyList<NavItem>)items.OrderBy(i => i.Nav.Order).ToList();
            },
            ct
        );

    /// <summary>Whether the umbrella policy lets the caller into the panel at all.</summary>
    public Task<bool> CanEnterAsync(CancellationToken ct = default) =>
        Can(_ => Task.FromResult(true), ct);

    /// <summary>Whether the caller may do <paramref name="op"/> on the table at all; for listing what a surface offers.</summary>
    public Task<bool> CanAsync(string tableId, AdminOp op, CancellationToken ct = default) =>
        Can(o => o.AllowsAsync(Table(tableId), op, null), ct);

    public Task<bool> CanActionAsync(
        string tableId,
        string actionId,
        CancellationToken ct = default
    ) => Can(o => o.AllowsAsync(Table(tableId), Action(Table(tableId), actionId), null), ct);

    public Task<bool> CanFormAsync(string formId, CancellationToken ct = default) =>
        Can(o => o.AllowsAsync(new FormRequest(o.User, Form(formId).Id)), ct);

    public Task<bool> CanDashboardAsync(string dashboardId, CancellationToken ct = default) =>
        Can(o => o.AllowsAsync(new DashboardRequest(o.User, Dashboard(dashboardId).Id)), ct);

    public Task<bool> CanPageAsync(string pageId, CancellationToken ct = default) =>
        Can(o => o.AllowsAsync(new PageRequest(o.User, pageId)), ct);

    private async Task<bool> Can(Func<Operation, Task<bool>> ask, CancellationToken ct)
    {
        try
        {
            return await runner.RunAsync("Checking access", ask, ct);
        }
        // A policy that fails is logged by the runner and answers no, so the shell still renders.
        catch (AdminRejection)
        {
            return false;
        }
    }

    private async Task<DashboardModel> OpenDashboardAsync(Operation op, string id)
    {
        var d = Dashboard(id);
        await op.EnsureAsync(
            op.AllowsAsync(new DashboardRequest(op.User, d.Id)),
            $"open {d.Title}"
        );
        return d;
    }

    private async Task<FormModel> OpenFormAsync(Operation op, string id)
    {
        var form = Form(id);
        await op.EnsureAsync(
            op.AllowsAsync(new FormRequest(op.User, form.Id)),
            $"submit {form.Title}"
        );
        return form;
    }

    private TableModel Table(string id) =>
        model.Table(id) ?? throw new AdminRejection($"There is no table '{id}'.");

    private FormModel Form(string id) =>
        model.Form(id) ?? throw new AdminRejection($"There is no form '{id}'.");

    private DashboardModel Dashboard(string id) =>
        model.Dashboard(id) ?? throw new AdminRejection($"There is no dashboard '{id}'.");

    private static ActionModel Action(TableModel t, string id) =>
        t.Action(id) ?? throw new AdminRejection($"Table '{t.Id}' has no action '{id}'.");

    private static IReadOnlyList<FieldModel> Fields(TableModel t, bool creating) =>
        t.Editable(creating).Select(c => c.Edit!).ToList();

    private static async Task<object?> FindAsync(Operation op, TableModel t, string key)
    {
        var find = t.Source.Find ?? throw new AdminRejection($"{t.Title} has no detail page.");
        return await find(op.Services, t.Key.Decode(key), op.CancellationToken);
    }

    private static async Task<object> RequireAsync(Operation op, TableModel t, string key) =>
        await FindAsync(op, t, key)
        ?? throw new AdminRejection($"{t.Title} has no row with the key '{key}'.");

    private async Task<(IReadOnlyList<object> Rows, int Total)> QueryAsync(
        Operation op,
        TableModel t,
        ListRequest r
    )
    {
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, raw) in r.Filters ?? new Dictionary<string, object?>())
        {
            var column = t.Column(name);
            if (column is not { Filterable: true })
                throw new AdminRejection($"{t.Title} cannot be filtered by '{name}'.");
            if (!ScalarTypes.TryCoerce(raw, column.ClrType, out var value))
                throw new AdminRejection($"'{raw}' is not a valid {column.Title}.");
            if (value is not null)
                filters[name] = value;
        }
        if (r.SortBy is { } sort && t.Column(sort) is not { Sortable: true })
            throw new AdminRejection($"{t.Title} cannot be sorted by '{sort}'.");
        var search = t.Searchable && !string.IsNullOrWhiteSpace(r.Search) ? r.Search.Trim() : null;
        // Find answers no filter and no row policy, so the row must pass both here.
        if (
            search is not null
            && filters.Count == 0
            && t.Source is { FindsKey: true, Find: { } find }
            && t.Key.TryDecode(search) is { } key
            && await find(op.Services, key, op.CancellationToken) is { } found
            && await op.AllowsAsync(t, AdminOp.Read, found)
        )
            return ([found], 1);
        return await t.Source.List(
            op.Services,
            r with
            {
                Search = search,
                Filters = filters,
                Exact = t.Exact,
            },
            op.CancellationToken
        );
    }

    private async Task<RowView> ViewAsync(
        Operation op,
        TableModel t,
        object row,
        bool listed,
        Dictionary<(string, string), string?> labels
    )
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var c in t.Columns)
        {
            if (listed && !c.InList)
                continue;
            values[c.Name] = c.Compute is { } compute
                ? await compute(op.Services, row, op.CancellationToken)
                : c.Read(row);
        }
        var actions = new HashSet<string>(StringComparer.Ordinal);
        if (t.HasDetail && !listed)
            foreach (var a in t.Actions)
                if (await op.AllowsAsync(t, a, row))
                    actions.Add(a.Id);
        var allowed = new Allowed(
            t.Source.Update is not null && await op.AllowsAsync(t, AdminOp.Update, row),
            t.Source.Delete is not null && await op.AllowsAsync(t, AdminOp.Delete, row),
            actions
        );
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in t.Columns)
            if (
                c.LinksTo is { } target
                && values.GetValueOrDefault(c.Name) is { } value
                && await LabelAsync(op, target, value, labels) is { } label
            )
                links[c.Name] = label;
        return new RowView(t.Key.Encode(row), t.Label(row), values, allowed) { Links = links };
    }

    /// <summary>The linked row's label; null when it is missing or the caller may not read it.</summary>
    private async Task<string?> LabelAsync(
        Operation op,
        string tableId,
        object value,
        Dictionary<(string, string), string?> seen
    )
    {
        var key = Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        if (seen.TryGetValue((tableId, key), out var known))
            return known;
        string? label = null;
        if (
            model.Table(tableId) is { Source.Find: { } find } target
            && target.Key.TryDecode(key) is { } parts
            && await find(op.Services, parts, op.CancellationToken) is { } row
            && await op.AllowsAsync(target, AdminOp.Read, row)
        )
            label = target.Label(row);
        return seen[(tableId, key)] = label;
    }

    private static async Task<FormView> FormAsync(
        Operation op,
        IReadOnlyList<FieldModel> fields,
        Func<FieldModel, object?> value
    )
    {
        var options = new Dictionary<string, IReadOnlyList<AdminOption>>(StringComparer.Ordinal);
        foreach (var f in fields)
            if (f.Options is { } load)
                options[f.Name] = await load(op.Services, op.CancellationToken);
        return new FormView(fields, fields.ToDictionary(f => f.Name, value), options);
    }

    private async Task<WidgetView> WidgetAsync(Operation op, WidgetModel widget)
    {
        try
        {
            switch (widget)
            {
                case StatModel stat:
                    return new StatView(
                        stat.Id,
                        stat.Title,
                        await stat.Fetch(op.Services, op.CancellationToken),
                        stat.Suffix
                    );
                case ChartModel chart:
                    var points = chart.Points is null
                        ? []
                        : await chart.Points(op.Services, op.CancellationToken);
                    return new ChartView(
                        chart.Id,
                        chart.Title,
                        points.Select(p => (chart.X(p), Number(chart.Y(p)))).ToList(),
                        chart.YLabel
                    );
                case TableWidgetModel tw:
                    var t = Table(tw.TableId);
                    await op.EnsureAsync(op.AllowsAsync(t, AdminOp.Read, null), $"read {t.Title}");
                    var columns = tw.Columns ?? t.ListColumns.Select(c => c.Name).ToList();
                    var (rows, _) = await t.Source.List(
                        op.Services,
                        new ListRequest(0, tw.Take, null, tw.SortBy, tw.Descending),
                        op.CancellationToken
                    );
                    var views = new List<RowView>();
                    var labels = new Dictionary<(string, string), string?>();
                    foreach (var row in rows)
                        views.Add(await ViewAsync(op, t, row, listed: false, labels));
                    return new TableWidgetView(tw.Id, tw.Title, t.Id, columns, views);
                default:
                    throw new InvalidOperationException($"Unknown widget {widget.GetType().Name}.");
            }
        }
        catch (Exception ex)
            when (!(
                    ex is OperationCanceledException && op.CancellationToken.IsCancellationRequested
                )
            )
        {
            var error = ex is AdminRejection ? ex.Message : "This widget failed to load.";
            return widget switch
            {
                StatModel s => new StatView(s.Id, s.Title, null, s.Suffix) { Error = error },
                ChartModel c => new ChartView(c.Id, c.Title, [], c.YLabel) { Error = error },
                _ => new TableWidgetView(widget.Id, widget.Title, "", [], []) { Error = error },
            };
        }
    }

    private static double Number(object? value) =>
        value is null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>A row's stored scalar values, the ones an audit diff compares.</summary>
    private static Dictionary<string, object?> Snapshot(object row) =>
        row.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead
                && p.GetIndexParameters().Length == 0
                && ScalarTypes.IsScalar(p.PropertyType)
            )
            .ToDictionary(p => p.Name, p => p.GetValue(row), StringComparer.Ordinal);

    /// <summary>An input as the audit records it: a file by its name, type and size.</summary>
    private static Dictionary<string, object?> Logged(Dictionary<string, object?> input) =>
        input.ToDictionary(
            kv => kv.Key,
            kv =>
                kv.Value is AdminFile f ? $"{f.Name} ({f.ContentType}, {f.Length} bytes)" : kv.Value
        );
}
