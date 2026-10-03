using System.Net;
using System.Text;
using AdminForge.Core.Metadata;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Mcp;

/// <summary>What a connection may call: the tools the admin may run, grouped as the sidebar groups them.</summary>
internal static class ConsentPage
{
    public static IResult Render(
        string title,
        string clientName,
        IReadOnlyList<PanelTool> tools,
        IReadOnlyList<TimeSpan> lifetimes,
        IReadOnlyDictionary<string, string> request,
        AntiforgeryTokenSet antiforgery,
        string action
    )
    {
        var hidden = new StringBuilder();
        hidden.Append(
            $"""<input type="hidden" name="{antiforgery.FormFieldName}" value="{antiforgery.RequestToken}">"""
        );
        foreach (var (name, value) in request)
            hidden.Append($"""<input type="hidden" name="{E(name)}" value="{E(value)}">""");

        var groups = new StringBuilder();
        foreach (var group in NavGrouping.Arrange(tools, t => t.Nav, t => t.Section))
        {
            groups.Append("<fieldset>");
            if (group.Key is not null)
                groups.Append($"<legend>{E(group.Key)}</legend>");
            foreach (var section in group.Entries.GroupBy(t => t.Section))
            {
                groups.Append($"<h3>{E(section.Key)}</h3>");
                foreach (var tool in section)
                {
                    var t = tool.ProtocolTool;
                    var ro = t.Annotations?.ReadOnlyHint == true ? " data-readonly" : "";
                    groups.Append(
                        $"""<label title="{E(t.Description ?? "")}"><input type="checkbox" name="tools" value="{E(t.Name)}"{ro}> {E(t.Name)}</label>"""
                    );
                }
            }
            groups.Append("</fieldset>");
        }

        var expiry = new StringBuilder();
        foreach (var span in lifetimes)
            expiry.Append(
                $"""<option value="{(int)span.TotalMinutes}">{Describe(span)}</option>"""
            );

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{E(title)}} — connect {{E(clientName)}}</title>
              <style>
                body { font-family: system-ui, sans-serif; background: #f4f5f7; margin: 0; padding: 4vh 1rem; }
                form { max-width: 40rem; margin: 0 auto; background: #fff; padding: 2rem; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,.08); }
                h1 { font-size: 1.25rem; margin: 0 0 .25rem; } h3 { font-size: .9rem; margin: .75rem 0 .25rem; color: #555; }
                fieldset { border: 1px solid #ddd; border-radius: 6px; margin: 1rem 0; padding: .5rem 1rem; }
                legend { font-weight: 600; padding: 0 .4rem; }
                label { display: block; font-family: ui-monospace, monospace; font-size: .85rem; padding: .15rem 0; }
                .presets button, .decide button { padding: .5rem .9rem; font-size: .95rem; border-radius: 4px; border: 1px solid #ccc; background: #fff; cursor: pointer; margin-right: .5rem; }
                .decide { margin-top: 1.5rem; display: flex; align-items: center; gap: .5rem; }
                .allow { background: #1976d2 !important; color: #fff; border-color: #1976d2 !important; }
                select { padding: .4rem; font-size: .95rem; }
              </style>
            </head>
            <body>
              <form method="post" action="{{action}}">
                {{hidden}}
                <h1>{{E(title)}}</h1>
                <p><strong>{{E(
                clientName
            )}}</strong> asks to use the panel as you. Tick what it may call.</p>
                <div class="presets">
                  <button type="button" onclick="pick(e => e.hasAttribute('data-readonly'))">Read only</button>
                  <button type="button" onclick="pick(() => true)">Everything</button>
                  <button type="button" onclick="pick(() => false)">Nothing</button>
                </div>
                {{groups}}
                <div class="decide">
                  <label for="expires" style="font-family: inherit">Expires in</label>
                  <select id="expires" name="expires">{{expiry}}</select>
                  <span style="flex:1"></span>
                  <button type="submit" name="decision" value="deny">Deny</button>
                  <button type="submit" name="decision" value="allow" class="allow">Allow</button>
                </div>
              </form>
              <script>
                function pick(f) { document.querySelectorAll('input[name=tools]').forEach(e => e.checked = f(e)); }
                pick(e => e.hasAttribute('data-readonly'));
              </script>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0} day(s)" : $"{span.TotalHours:0} hour(s)";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
