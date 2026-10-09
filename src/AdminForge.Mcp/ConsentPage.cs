using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace AdminForge.Mcp;

/// <summary>What a connection may call: the tools the admin may run, grouped as the sidebar groups them.</summary>
internal static class ConsentPage
{
    public static IResult Render(
        PanelModel panel,
        string clientName,
        Uri redirect,
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
        foreach (var group in NavGrouping.Arrange(tools, t => t.Nav))
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
                        $"""<label class="af-check" title="{E(t.Description ?? "")}"><input type="checkbox" name="tools" value="{E(t.Name)}"{ro}> {E(t.Name)}</label>"""
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

        // The client names itself at registration, so its name proves nothing; the redirect host is where the code goes.
        var target =
            $"""<p>The connection is handed to <strong class="af-mono" data-af="redirect-host">{E(redirect.Authority)}</strong>.</p>"""
            + (
                redirect.IsLoopback
                    ? ""
                    : $"""<div class="af-alert af-alert-danger" role="alert" data-af="remote-redirect">That is not this computer. Allow only if you know {E(redirect.Host)}.</div>"""
            );
        var form = $$"""
                <form class="af-panel af-auth-card af-wide af-consent" method="post" action="{{action}}">
                  {{hidden}}
                  <div class="af-auth-brand"><h1>{{E(panel.Title)}}</h1>{{UI.Theme.EnvironmentBadge(panel)}}</div>
                  <p><strong>{{E(
                clientName
            )}}</strong> asks to use the panel as you. Tick what it may call.</p>
                  {{target}}
                  <div class="af-row" role="group" aria-label="Presets">
                    <button class="af-btn" type="button" onclick="pick(e => e.hasAttribute('data-readonly'))">Read only</button>
                    <button class="af-btn" type="button" onclick="pick(() => true)">Everything</button>
                    <button class="af-btn" type="button" onclick="pick(() => false)">Nothing</button>
                  </div>
                  {{groups}}
                  <div class="af-row">
                    <label class="af-label" for="expires">Expires in</label>
                    <select class="af-select" id="expires" name="expires">{{expiry}}</select>
                    <span class="af-spacer"></span>
                    <button class="af-btn" type="submit" name="decision" value="deny">Deny</button>
                    <button class="af-btn af-btn-primary" type="submit" name="decision" value="allow">Allow</button>
                  </div>
                </form>
                <script>
                  function pick(f) { document.querySelectorAll('input[name=tools]').forEach(e => e.checked = f(e)); }
                  pick(e => e.hasAttribute('data-readonly'));
                </script>
            """;
        return Results.Content(
            UI.Theme.PlainPage(panel, $"Connect {clientName} · {panel.Title}", form),
            "text/html; charset=utf-8"
        );
    }

    private static string Describe(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{span.TotalDays:0} day(s)" : $"{span.TotalHours:0} hour(s)";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
