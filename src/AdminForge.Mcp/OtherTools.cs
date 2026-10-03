using System.Text.Json.Nodes;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;
using ModelContextProtocol.Protocol;

namespace AdminForge.Mcp;

internal static class SelectChoices
{
    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<SelectOption>>> LoadAsync(
        IAdminUIBridge bridge,
        IEnumerable<FieldMeta> fields,
        CancellationToken ct
    )
    {
        var choices = new Dictionary<string, IReadOnlyList<SelectOption>>(StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Kind == FieldKind.Select))
            choices[f.Name] = await bridge.LoadSelectOptionsAsync(f, ct).ConfigureAwait(false);
        return choices;
    }

    public static FormSubmission Submission(IEnumerable<FieldMeta> fields, ToolArgs args)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Kind != FieldKind.FileUpload))
            if (args.TryGet(f.Name, out var e))
                values[f.Name] = JsonSchema.FieldValue(f, e);
        return new FormSubmission(values);
    }
}

internal sealed class FormTool(FormMeta form, AdminForgeMcpOptions options) : PanelTool(options)
{
    public override NavMeta Nav => form.Nav;
    public override string Section => form.Nav.Label ?? form.Title;

    private readonly string _name = $"form_{ToolNames.Slug(form.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge) => Describe(choices: null);

    private Tool Describe(IReadOnlyDictionary<string, IReadOnlyList<SelectOption>>? choices)
    {
        var p = new JsonObject();
        foreach (var f in form.Fields.Where(f => f.Kind != FieldKind.FileUpload))
            p[f.Name] = JsonSchema.ForField(f, choices?.GetValueOrDefault(f.Name));
        return Tool(
            _name,
            $"Submits the '{form.Title}' form. {form.Description}".Trim(),
            JsonSchema.Object(p, form.Fields.Where(f => f.Required).Select(f => f.Name)),
            destructive: true
        );
    }

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(form, ct);

    public override async Task RefreshAsync(IAdminUIBridge bridge, CancellationToken ct)
    {
        if (!form.Fields.Any(f => f.Kind == FieldKind.Select))
            return;
        Replace(
            Describe(await SelectChoices.LoadAsync(bridge, form.Fields, ct).ConfigureAwait(false))
        );
    }

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var context = new McpActionContext();
        await bridge
            .SubmitFormAsync(
                form.RouteName,
                SelectChoices.Submission(form.Fields, args),
                context,
                ct
            )
            .ConfigureAwait(false);
        var text = context.Text.Length > 0 ? context.Text : "Submitted.";
        return context.Failed ? Error(text) : Text(text);
    }
}

internal sealed class DashboardTool(DashboardMeta dashboard, AdminForgeMcpOptions options)
    : PanelTool(options)
{
    public override NavMeta Nav => dashboard.Nav;
    public override string Section => dashboard.Nav.Label ?? dashboard.Title;

    private readonly string _name = $"dashboard_{ToolNames.Slug(dashboard.RouteName)}";

    protected override Tool Describe(IAdminUIBridge? bridge) =>
        Tool(
            _name,
            $"Loads the '{dashboard.Title}' dashboard: {string.Join(", ", dashboard.Widgets.Select(w => w.Title))}.",
            JsonSchema.Object(new JsonObject(), []),
            readOnly: true
        );

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        bridge.IsAllowedAsync(dashboard, ct);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var vm = await bridge.LoadDashboardAsync(dashboard, ct).ConfigureAwait(false);
        var widgets = new JsonObject();
        foreach (var (id, w) in vm.Widgets)
        {
            var o = new JsonObject { ["title"] = w.Title, ["kind"] = w.Kind.ToString() };
            if (w.Error is not null)
                o["error"] = w.Error;
            switch (w)
            {
                case StatCardVM s:
                    o["value"] = System.Text.Json.JsonSerializer.SerializeToNode(s.Value, Json);
                    if (s.Suffix is not null)
                        o["suffix"] = s.Suffix;
                    break;
                case LineChartVM l:
                    o["points"] = new JsonArray([
                        .. l.Points.Select(p =>
                            (JsonNode)
                                new JsonObject
                                {
                                    ["x"] = System.Text.Json.JsonSerializer.SerializeToNode(
                                        p.X,
                                        Json
                                    ),
                                    ["y"] = p.Y,
                                }
                        ),
                    ]);
                    break;
                case TableWidgetVM t:
                    o["entity"] = t.EntityMeta.RouteName;
                    o["rows"] = new JsonArray([
                        .. t.Rows.Select(r =>
                            (JsonNode)Row(t.EntityMeta, r.Key, r.Values, listed: true)
                        ),
                    ]);
                    break;
            }
            widgets[id] = o;
        }
        return JsonResult(new JsonObject { ["title"] = vm.Title, ["widgets"] = widgets });
    }
}

internal sealed class SearchTool(AdminForgeMcpOptions options) : PanelTool(options)
{
    private static readonly NavMeta Ungrouped = new();
    public override NavMeta Nav => Ungrouped;
    public override string Section => "Search";

    protected override Tool Describe(IAdminUIBridge? bridge) =>
        Tool(
            "search",
            "Free-text search across every searchable table the caller may read; returns keys and labels.",
            JsonSchema.Object(
                new JsonObject
                {
                    ["text"] = JsonSchema.Property("string"),
                    ["take_per_entity"] = JsonSchema.Property(
                        "integer",
                        "Hits per table, default 5."
                    ),
                },
                ["text"]
            ),
            readOnly: true
        );

    public override Task<bool> IsAllowedAsync(IAdminUIBridge bridge, CancellationToken ct) =>
        Task.FromResult(true);

    protected override async Task<CallToolResult> RunAsync(
        IAdminUIBridge bridge,
        ToolArgs args,
        CancellationToken ct
    )
    {
        var hits = await bridge
            .SearchAsync(args.Required("text"), args.Int("take_per_entity") ?? 5, ct)
            .ConfigureAwait(false);
        return JsonResult(
            hits.GroupBy(h => h.EntityRouteName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(h => new { key = h.Row.Key, label = h.Row.DisplayLabel }).ToList()
                )
        );
    }
}
