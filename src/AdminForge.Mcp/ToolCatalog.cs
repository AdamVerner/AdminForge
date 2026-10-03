using AdminForge.Core.Configuration;
using AdminForge.Core.Metadata;
using Microsoft.Extensions.Logging;

namespace AdminForge.Mcp;

internal static class ToolCatalog
{
    /// <summary>Every tool the registered panel yields, in sidebar order.</summary>
    public static List<PanelTool> Build(
        AdminForgeOptions forge,
        AdminForgeMcpOptions options,
        ILogger logger
    )
    {
        var tools = new List<PanelTool> { new SearchTool(options) };
        foreach (var e in forge.Entities.Where(e => !e.IsJoinEntity))
        {
            tools.Add(new ListTool(e, options));
            tools.Add(new GetTool(e, options));
            if (!e.IsReadOnly)
            {
                tools.Add(new CreateTool(e, options));
                tools.Add(new UpdateTool(e, options));
            }
            if (e.CustomDeleteHandler is not null)
                tools.Add(new DeleteTool(e, options));
            foreach (var a in e.Actions)
            {
                if (HasFile(a.Fields, $"{e.Name} action '{a.Name}'", logger))
                    continue;
                tools.Add(new ActionTool(e, a, options));
            }
        }
        foreach (var f in forge.Forms)
            if (!HasFile(f.Fields, $"form '{f.RouteName}'", logger))
                tools.Add(new FormTool(f, options));
        foreach (var d in forge.Dashboards)
            tools.Add(new DashboardTool(d, options));
        return tools;
    }

    /// <summary>A file cannot travel in a tool call: an optional one is left out, a required one drops the tool.</summary>
    private static bool HasFile(IEnumerable<FieldMeta> fields, string what, ILogger logger)
    {
        if (!fields.Any(f => f is { Kind: FieldKind.FileUpload, Required: true }))
            return false;
        logger.LogWarning("AdminForge MCP: {What} requires a file upload and gets no tool.", what);
        return true;
    }
}
