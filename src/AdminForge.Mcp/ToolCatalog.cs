using Microsoft.Extensions.Logging;

namespace AdminForge.Mcp;

/// <summary>Every tool the registered panel yields, built once.</summary>
internal sealed class PanelTools(IReadOnlyList<PanelTool> all)
{
    public IReadOnlyList<PanelTool> All { get; } = all;

    /// <summary>Every tool the panel yields: a list per table, a get, writes and actions as its source and model offer them.</summary>
    public static PanelTools Build(PanelModel panel, ILogger logger)
    {
        var tools = new List<PanelTool> { new SearchTool() };
        foreach (var t in panel.Tables)
        {
            tools.Add(new ListTool(t));
            if (t.HasDetail)
                tools.Add(new GetTool(t));
            if (t.Source.Create is not null)
                tools.Add(new CreateTool(t));
            if (t.Source.Update is not null)
                tools.Add(new UpdateTool(t));
            if (t.Source.Delete is not null)
                tools.Add(new DeleteTool(t));
            foreach (var a in t.Actions)
                if (!NeedsFile(a.Input?.Fields ?? [], $"{t.Id} action '{a.Id}'", logger))
                    tools.Add(new ActionTool(t, a));
        }
        foreach (var f in panel.Forms)
            if (!NeedsFile(f.Input.Fields, $"form '{f.Id}'", logger))
                tools.Add(new FormTool(f));
        foreach (var d in panel.Dashboards)
            tools.Add(new DashboardTool(d));
        if (tools.GroupBy(t => t.ProtocolTool.Name).FirstOrDefault(g => g.Count() > 1) is { } twin)
            throw new InvalidOperationException(
                $"Two MCP tools are named '{twin.Key}'; give one of their tables, actions, forms or dashboards another id."
            );
        return new PanelTools(tools);
    }

    /// <summary>A file cannot travel in a tool call: an optional one is left out, a required one drops the tool.</summary>
    private static bool NeedsFile(IEnumerable<FieldModel> fields, string what, ILogger logger)
    {
        if (!fields.Any(f => f is { Kind: FieldKind.File, Required: true }))
            return false;
        logger.LogWarning("AdminForge MCP: {What} requires a file upload and gets no tool.", what);
        return true;
    }
}
