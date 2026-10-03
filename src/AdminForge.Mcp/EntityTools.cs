using System.Text.Json.Nodes;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using ModelContextProtocol.Protocol;

namespace AdminForge.Mcp;

internal static class EntityColumns
{
    public static bool IsScalar(ColumnMeta c) => c.Kind is ColumnKind.Scalar or ColumnKind.Enum;

    /// <summary>A timestamp filter matches to the tick, which no agent means, so it is not offered.</summary>
    public static bool IsFilter(ColumnMeta c)
    {
        var t = Nullable.GetUnderlyingType(c.ClrType) ?? c.ClrType;
        return c.IsFilterable
            && IsScalar(c)
            && !c.IsCustom
            && t != typeof(DateTime)
            && t != typeof(DateTimeOffset)
            && t != typeof(DateOnly);
    }

    /// <summary>What create and update accept: scalar, not generated, not hidden from the form, not the key.</summary>
    public static IEnumerable<ColumnMeta> Editable(EntityMeta e) =>
        e.OrderedColumns.Where(c =>
            IsScalar(c) && !c.IsGenerated && !c.HiddenInEdit && !c.IsPrimaryKey && !c.IsCustom
        );

    public static JsonObject Properties(IEnumerable<ColumnMeta> columns)
    {
        var p = new JsonObject();
        foreach (var c in columns)
            p[c.PropertyName] = JsonSchema.ForClrType(c.ClrType, c.Description ?? c.Label);
        return p;
    }
}

internal sealed class ListTool(EntityMeta entity, AdminForgeMcpOptions options) : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name = $"list_{ToolNames.Slug(entity.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge)
    {
        var sortable = entity
            .OrderedColumns.Where(c => c.IsSortable && EntityColumns.IsScalar(c))
            .Select(c => c.PropertyName)
            .ToList();
        var filterable = entity.OrderedColumns.Where(EntityColumns.IsFilter);
        var p = new JsonObject
        {
            ["page"] = JsonSchema.Property("integer", "Zero-based page."),
            ["page_size"] = JsonSchema.Property(
                "integer",
                $"Rows per page, default {Options.DefaultPageSize}, at most {Options.MaxPageSize}."
            ),
        };
        if (sortable.Count > 0)
        {
            p["sort_by"] = JsonSchema.Enum(sortable, "Column to sort by.");
            p["descending"] = JsonSchema.Property("boolean");
        }
        if (entity.IsSearchable)
            p["search"] = JsonSchema.Property("string", "Free text over the text columns.");
        foreach (var c in filterable)
            p[c.PropertyName] = JsonSchema.ForClrType(
                c.ClrType,
                $"Filter by {c.Label}" + (c.ClrType == typeof(string) ? ", contains." : ", equals.")
            );
        var listed = string.Join(
            ", ",
            entity.OrderedColumns.Where(c => c.ShowInList).Select(c => c.PropertyName)
        );
        return Tool(
            _name,
            $"Lists {entity.Label} rows: total count and one page with the list columns ({listed}). "
                + $"get_{ToolNames.Slug(entity.RouteName)} has every column.",
            JsonSchema.Object(p, []),
            readOnly: true,
            idempotent: true
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(entity, AdminAction.Read, cancellationToken: ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var c in entity.OrderedColumns.Where(EntityColumns.IsFilter))
            if (args.TryGet(c.PropertyName, out var e))
                filters[c.PropertyName] = JsonSchema.Scalar(e);

        var query = new ListQuery
        {
            Page = Math.Max(0, args.Int("page") ?? 0),
            PageSize = Math.Clamp(
                args.Int("page_size") ?? Options.DefaultPageSize,
                1,
                Options.MaxPageSize
            ),
            SortBy = args.String("sort_by"),
            SortDescending = args.Bool("descending"),
            Search = entity.IsSearchable ? args.String("search") : null,
            Filters = filters,
        };
        var list = await bridge.ListAsync(entity, query, ct).ConfigureAwait(false);
        return JsonResult(
            new JsonObject
            {
                ["total"] = list.TotalCount,
                ["page"] = list.Page,
                ["page_size"] = list.PageSize,
                ["rows"] = new JsonArray([
                    .. list.Rows.Select(r => (JsonNode)Row(entity, r.Key, r.Values, listed: true)),
                ]),
            }
        );
    }
}

internal sealed class GetTool(EntityMeta entity, AdminForgeMcpOptions options) : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name = $"get_{ToolNames.Slug(entity.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge)
    {
        var related = string.Join(", ", entity.RelatedLinks.Select(l => l.Label));
        return Tool(
            _name,
            $"One {entity.Label} row by key, with every column and, under 'related', the first "
                + $"{Options.RelatedRows} rows of each child table"
                + (related.Length > 0 ? $" ({related}, and one per collection)." : "."),
            JsonSchema.Object(
                new JsonObject
                {
                    ["key"] = JsonSchema.Property("string", "The row key as list_ shows it."),
                },
                ["key"]
            ),
            readOnly: true,
            idempotent: true
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(entity, AdminAction.Read, cancellationToken: ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var key = args.Required("key");
        var view = await bridge.FindAsync(entity, key, ct).ConfigureAwait(false);
        if (view is null)
            return NotFound(key);

        var row = Row(entity, view.Key, view.Values, listed: false);
        if (view.RelatedLinks.Count > 0)
        {
            var related = new JsonObject();
            foreach (var link in view.RelatedLinks)
            {
                var target = bridge.FindEntityByRouteName(link.RouteName);
                if (target is null)
                    continue;
                var page = await bridge
                    .ListAsync(
                        target,
                        new ListQuery { Filters = link.Filter, PageSize = Options.RelatedRows },
                        ct
                    )
                    .ConfigureAwait(false);
                // A generated link's label counts the rows; the key must not change with them.
                var name = link.Generated ? target.Label : link.Label;
                for (var i = 2; related.ContainsKey(name); i++)
                    name = $"{(link.Generated ? target.Label : link.Label)} {i}";
                related[name] = new JsonObject
                {
                    ["entity"] = link.RouteName,
                    ["total"] = page.TotalCount,
                    ["rows"] = new JsonArray([
                        .. page.Rows.Select(r =>
                            (JsonNode)Row(target, r.Key, r.Values, listed: true)
                        ),
                    ]),
                };
            }
            row["related"] = related;
        }
        return JsonResult(row);
    }
}

internal sealed class ActionTool(EntityMeta entity, ActionMeta action, AdminForgeMcpOptions options)
    : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name =
        $"{ToolNames.Slug(entity.RouteName)}_{ToolNames.Slug(action.Name)}";

    protected override Tool Describe(IAdminUIBridge? bridge) => Describe(choices: null);

    private Tool Describe(IReadOnlyDictionary<string, IReadOnlyList<SelectOption>>? choices)
    {
        var p = new JsonObject
        {
            ["key"] = JsonSchema.Property("string", $"The {entity.Label} row key."),
        };
        foreach (var f in action.Fields.Where(f => f.Kind != FieldKind.FileUpload))
            p[f.Name] = JsonSchema.ForField(f, choices?.GetValueOrDefault(f.Name));
        var required = new[] { "key" }.Concat(
            action.Fields.Where(f => f.Required).Select(f => f.Name)
        );
        var confirm = action.ConfirmationMessage is null ? "" : $" {action.ConfirmationMessage}";
        return Tool(
            _name,
            $"Runs '{action.Name}' on one {entity.Label} row.{confirm}",
            JsonSchema.Object(p, required),
            destructive: true
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(
            entity,
            AdminAction.Custom,
            actionName: action.Name,
            cancellationToken: ct
        );

    public override async Task RefreshAsync(IAdminUIBridge bridge, CancellationToken ct)
    {
        if (!action.Fields.Any(f => f.Kind == FieldKind.Select))
            return;
        Replace(
            Describe(await SelectChoices.LoadAsync(bridge, action.Fields, ct).ConfigureAwait(false))
        );
    }

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var key = args.Required("key");
        var context = new McpActionContext();
        await bridge
            .InvokeActionAsync(
                entity.RouteName,
                key,
                action.Name,
                context,
                SelectChoices.Submission(action.Fields, args),
                ct
            )
            .ConfigureAwait(false);
        var text = context.Text.Length > 0 ? context.Text : "Done.";
        return context.Failed ? Error(text) : Text(text);
    }
}

internal sealed class CreateTool(EntityMeta entity, AdminForgeMcpOptions options)
    : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name = $"create_{ToolNames.Slug(entity.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge)
    {
        var editable = EntityColumns.Editable(entity).ToList();
        return Tool(
            _name,
            $"Creates a {entity.Label} row and returns its key.",
            JsonSchema.Object(
                EntityColumns.Properties(editable),
                editable.Where(c => c.IsRequired).Select(c => c.PropertyName)
            )
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(entity, AdminAction.Create, cancellationToken: ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var model = bridge.NewEditModel(entity);
        Apply(entity, args, model.Values);
        var key = await bridge
            .CreateAsync(entity, model, new McpActionContext(), ct)
            .ConfigureAwait(false);
        return JsonResult(new JsonObject { ["key"] = key });
    }

    internal static void Apply(
        EntityMeta entity,
        ToolArgs args,
        IDictionary<string, object?> values
    )
    {
        foreach (var c in EntityColumns.Editable(entity))
            if (args.TryGet(c.PropertyName, out var e))
                values[c.PropertyName] = JsonSchema.Scalar(e);
    }
}

internal sealed class UpdateTool(EntityMeta entity, AdminForgeMcpOptions options)
    : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name = $"update_{ToolNames.Slug(entity.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge)
    {
        var p = EntityColumns.Properties(EntityColumns.Editable(entity));
        p["key"] = JsonSchema.Property("string", "The row key.");
        return Tool(
            _name,
            $"Changes the given columns of one {entity.Label} row; the others stay.",
            JsonSchema.Object(p, ["key"]),
            idempotent: true
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(entity, AdminAction.Update, cancellationToken: ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var key = args.Required("key");
        var model = await bridge.LoadForEditAsync(entity, key, ct).ConfigureAwait(false);
        if (model is null)
            return NotFound(key);
        CreateTool.Apply(entity, args, model.Values);
        await bridge.UpdateAsync(entity, model, new McpActionContext(), ct).ConfigureAwait(false);
        return Text("Updated.");
    }
}

internal sealed class DeleteTool(EntityMeta entity, AdminForgeMcpOptions options)
    : PanelTool(options)
{
    public override NavMeta Nav => entity.Nav;
    public override string Section => entity.Nav.Label ?? entity.Label;

    private readonly string _name = $"delete_{ToolNames.Slug(entity.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge) =>
        Tool(
            _name,
            $"Deletes one {entity.Label} row.",
            JsonSchema.Object(
                new JsonObject { ["key"] = JsonSchema.Property("string", "The row key.") },
                ["key"]
            ),
            destructive: true
        );

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(entity, AdminAction.Delete, cancellationToken: ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var key = args.Required("key");
        var deleted = await bridge
            .DeleteAsync(entity, key, new McpActionContext(), ct)
            .ConfigureAwait(false);
        return deleted ? Text("Deleted.") : NotFound(key);
    }
}
