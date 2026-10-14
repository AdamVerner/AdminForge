using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace AdminForge.Mcp;

/// <summary>A tool over one table; its name is built from the table's stable id.</summary>
internal abstract class TableTool(TableModel table) : PanelTool
{
    protected TableModel Table { get; } = table;
    protected string Slug { get; } = PanelTool.Snake(table.Id);
    public override NavModel Nav => Table.Nav;
    public override string Section => Table.Title;

    protected JsonObject Fields(bool creating) =>
        JsonSchema.Fields(Table.Editable(creating).Select(c => c.Edit!));

    protected static JsonObject Key(JsonObject? properties = null, string what = "The row key.")
    {
        var p = properties ?? new JsonObject();
        p["key"] = JsonSchema.Property("string", what);
        return p;
    }
}

internal sealed class ListTool(TableModel table, int panelMaxPageSize) : TableTool(table)
{
    private const int DefaultPageSize = 25;
    private readonly int MaxPageSize = Math.Min(200, panelMaxPageSize);

    private IEnumerable<ColumnModel> Filters => Table.Columns.Where(c => c.Filterable);

    protected override Tool Describe()
    {
        var p = new JsonObject
        {
            ["page"] = JsonSchema.Property("integer", "Zero-based page."),
            ["page_size"] = JsonSchema.Property(
                "integer",
                $"Rows per page, default {DefaultPageSize}, at most {MaxPageSize}."
            ),
        };
        var sortable = Table.Columns.Where(c => c.Sortable).Select(c => c.Name).ToList();
        if (sortable.Count > 0)
        {
            p["sort_by"] = JsonSchema.Enum(sortable, "Column to sort by.");
            p["descending"] = JsonSchema.Property("boolean");
        }
        if (Table.Searchable)
            p["search"] = JsonSchema.Property("string", "Free text.");
        foreach (var c in Filters)
            p[c.Name] = JsonSchema.ForClrType(
                c.ClrType,
                $"Filter by {c.Title}"
                    + (
                        c.ClrType == typeof(string) && !Table.Exact.Contains(c.Name)
                            ? ", contains."
                            : ", equals."
                    )
            );
        var listed = string.Join(", ", Table.ListColumns.Select(c => c.Name));
        return Tool(
            $"list_{Slug}",
            $"Lists {Table.Title} rows: total count and one page with the list columns ({listed})."
                + (Table.HasDetail ? $" get_{Slug} has every column." : ""),
            JsonSchema.Object(p, []),
            readOnly: true,
            idempotent: true
        );
    }

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanAsync(Table.Id, AdminOp.Read, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var c in Filters)
            if (args.TryGet(c.Name, out var e))
                filters[c.Name] = JsonSchema.Scalar(e);
        var list = await panel.ListAsync(
            Table.Id,
            new ListRequest(
                args.Int("page") ?? 0,
                Math.Clamp(args.Int("page_size") ?? DefaultPageSize, 1, MaxPageSize),
                args.String("search"),
                args.String("sort_by"),
                args.Bool("descending"),
                filters
            ),
            ct
        );
        return JsonResult(
            new JsonObject
            {
                ["total"] = list.Total,
                ["page"] = list.Page,
                ["page_size"] = list.PageSize,
                ["rows"] = Rows(list.Rows),
            }
        );
    }
}

internal sealed class GetTool(TableModel table) : TableTool(table)
{
    private const int RelatedRows = 25;

    protected override Tool Describe()
    {
        var related = string.Join(", ", Table.Related.Select(r => r.Title));
        return Tool(
            $"get_{Slug}",
            $"One {Table.Title} row by key, with every column"
                + (
                    related.Length > 0
                        ? $" and, under 'related', the first {RelatedRows} rows of {related}."
                        : "."
                ),
            JsonSchema.Object(Key(what: "The row key as list_ shows it."), ["key"]),
            readOnly: true,
            idempotent: true
        );
    }

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanAsync(Table.Id, AdminOp.Read, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var key = args.Required("key");
        var view = await panel.GetAsync(Table.Id, key, ct);
        if (view is null)
            return Error($"{Table.Title} has no row with the key '{key}'.");
        var row = Row(view.Row);
        if (view.Related.Count > 0)
        {
            var related = new JsonObject();
            foreach (var link in view.Related.Where(l => args.GrantsRowsOf(l.Model.TableId)))
            {
                var page = await panel.ListAsync(
                    link.Model.TableId,
                    new ListRequest(PageSize: RelatedRows, Filters: link.Filter),
                    ct
                );
                related[link.Model.Title] = new JsonObject
                {
                    ["table"] = link.Model.TableId,
                    ["total"] = page.Total,
                    ["rows"] = Rows(page.Rows),
                };
            }
            row["related"] = related;
        }
        return JsonResult(row);
    }
}

internal sealed class CreateTool(TableModel table) : TableTool(table)
{
    protected override Tool Describe() =>
        Tool(
            $"create_{Slug}",
            $"Creates a {Table.Title} row and returns its key.",
            JsonSchema.Object(
                Fields(creating: true),
                Table
                    .Editable(creating: true)
                    .Select(c => c.Edit!)
                    .Where(f => f.Required)
                    .Select(f => f.Name)
            )
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanAsync(Table.Id, AdminOp.Create, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    ) =>
        JsonResult(
            new JsonObject { ["key"] = await panel.CreateAsync(Table.Id, args.Values(), ct) }
        );
}

internal sealed class UpdateTool(TableModel table) : TableTool(table)
{
    protected override Tool Describe() =>
        Tool(
            $"update_{Slug}",
            $"Changes the given columns of one {Table.Title} row; the others stay.",
            JsonSchema.Object(Key(Fields(creating: false)), ["key"]),
            idempotent: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanAsync(Table.Id, AdminOp.Update, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        await panel.UpdateAsync(Table.Id, args.Required("key"), args.Values(), ct);
        return Text("Updated.");
    }
}

internal sealed class DeleteTool(TableModel table) : TableTool(table)
{
    protected override Tool Describe() =>
        Tool(
            $"delete_{Slug}",
            $"Deletes one {Table.Title} row.",
            JsonSchema.Object(Key(), ["key"]),
            destructive: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanAsync(Table.Id, AdminOp.Delete, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        await panel.DeleteAsync(Table.Id, args.Required("key"), ct);
        return Text("Deleted.");
    }
}

/// <summary>
/// An action has no confirmation step over MCP: the client's call is the confirmation, so an action
/// with <c>Confirm(...)</c> runs and its prompt only joins the tool's description.
/// </summary>
internal sealed class ActionTool(TableModel table, ActionModel action) : TableTool(table)
{
    private IReadOnlyList<FieldModel> Inputs => action.Input?.Fields ?? [];

    protected override Tool Describe() => Describe(null);

    private Tool Describe(IReadOnlyDictionary<string, IReadOnlyList<AdminOption>>? choices) =>
        Tool(
            $"{Slug}_{PanelTool.Snake(action.Id)}",
            $"Runs '{action.Title}' on one {Table.Title} row.{(action.Confirm is null ? "" : " " + action.Confirm)}",
            JsonSchema.Object(
                Key(JsonSchema.Fields(Inputs, choices), $"The {Table.Title} row key."),
                Inputs.Where(f => f.Required).Select(f => f.Name).Prepend("key")
            ),
            destructive: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanActionAsync(Table.Id, action.Id, ct);

    public override async Task<Tool> DescribeAsync(AdminPanel panel, CancellationToken ct) =>
        Inputs.Any(f => f.Options is not null)
            ? Describe((await panel.ActionFormAsync(Table.Id, null, action.Id, ct)).Options)
            : ProtocolTool;

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    ) =>
        Text(
            await panel.RunActionAsync(
                Table.Id,
                args.Required("key"),
                action.Id,
                args.Values(),
                ct
            ),
            "Done."
        );
}

internal sealed class FormTool(FormModel form) : PanelTool
{
    public override NavModel Nav => form.Nav;
    public override string Section => form.Title;

    protected override Tool Describe() => Describe(null);

    private Tool Describe(IReadOnlyDictionary<string, IReadOnlyList<AdminOption>>? choices) =>
        Tool(
            $"form_{PanelTool.Snake(form.Id)}",
            $"Submits the '{form.Title}' form. {form.Description}".Trim(),
            JsonSchema.Object(
                JsonSchema.Fields(form.Input.Fields, choices),
                form.Input.Fields.Where(f => f.Required).Select(f => f.Name)
            ),
            destructive: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanFormAsync(form.Id, ct);

    public override async Task<Tool> DescribeAsync(AdminPanel panel, CancellationToken ct) =>
        form.Input.Fields.Any(f => f.Options is not null)
            ? Describe((await panel.FormAsync(form.Id, ct)).Options)
            : ProtocolTool;

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    ) => Text(await panel.SubmitFormAsync(form.Id, args.Values(), ct), "Submitted.");
}

internal sealed class DashboardTool(DashboardModel dashboard) : PanelTool
{
    public override NavModel Nav => dashboard.Nav;
    public override string Section => dashboard.Title;

    protected override Tool Describe() =>
        Tool(
            $"dashboard_{PanelTool.Snake(dashboard.Id)}",
            $"Loads the '{dashboard.Title}' dashboard: {string.Join(", ", dashboard.Rows.SelectMany(r => r).Select(w => w.Title))}.",
            JsonSchema.Object(new JsonObject(), []),
            readOnly: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanDashboardAsync(dashboard.Id, ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var view = await panel.DashboardAsync(dashboard.Id, ct);
        var widgets = new JsonObject();
        foreach (var (id, w) in view.Widgets)
        {
            var o = new JsonObject { ["title"] = w.Title };
            if (w.Error is not null)
                o["error"] = w.Error;
            switch (w)
            {
                case StatView s:
                    o["value"] = Value(s.Value);
                    if (s.Suffix is not null)
                        o["suffix"] = s.Suffix;
                    break;
                case ChartView c:
                    o["points"] = new JsonArray([
                        .. c.Points.Select(p =>
                            (JsonNode)new JsonObject { ["x"] = Value(p.X), ["y"] = p.Y }
                        ),
                    ]);
                    break;
                case TableWidgetView t:
                    o["table"] = t.TableId;
                    if (args.GrantsRowsOf(t.TableId))
                        o["rows"] = Rows(t.Rows);
                    else
                        o["error"] = $"This connection was not granted {t.TableId}.";
                    break;
            }
            widgets[id] = o;
        }
        return JsonResult(new JsonObject { ["title"] = dashboard.Title, ["widgets"] = widgets });
    }
}

internal sealed class SearchTool : PanelTool
{
    public override NavModel Nav { get; } = new(null, NavArea.Main, false, -1);
    public override string Section => "Search";

    protected override Tool Describe() =>
        Tool(
            "search",
            "Free-text search across every searchable table the caller may read; returns keys and labels.",
            JsonSchema.Object(
                new JsonObject
                {
                    ["text"] = JsonSchema.Property("string"),
                    ["take_per_table"] = JsonSchema.Property(
                        "integer",
                        "Hits per table, default 5."
                    ),
                },
                ["text"]
            ),
            readOnly: true
        );

    public override Task<bool> IsAllowedAsync(AdminPanel panel, CancellationToken ct) =>
        panel.CanEnterAsync(ct);

    protected override async Task<CallToolResult> RunAsync(
        AdminPanel panel,
        ToolArgs args,
        CancellationToken ct
    )
    {
        // SearchAsync skips refused tables, so without this a caller the umbrella refuses gets no hits instead of a refusal.
        if (!await panel.CanEnterAsync(ct))
            throw new AdminForbiddenException("search");
        var hits = await panel.SearchAsync(
            args.Required("text"),
            args.Int("take_per_table") ?? 5,
            ct
        );
        var result = new JsonObject();
        foreach (var g in hits.Where(h => args.GrantsRowsOf(h.TableId)).GroupBy(h => h.TableId))
            result[g.Key] = new JsonArray([
                .. g.Select(h => (JsonNode)new JsonObject { ["key"] = h.Key, ["label"] = h.Label }),
            ]);
        return JsonResult(result);
    }
}
