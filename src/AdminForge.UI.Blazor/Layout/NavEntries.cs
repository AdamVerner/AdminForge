using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace AdminForge.UI.Blazor.Layout;

/// <summary>One link the shell offers: an entity, dashboard, form or host page.</summary>
internal sealed record NavEntry(string Label, string Href, string Icon, NavMeta Nav);

internal static class NavEntries
{
    /// <summary>Every visible entry the signed-in admin may open, unordered.</summary>
    public static async Task<List<NavEntry>> CollectAsync(IAdminUIBridge bridge, AdminForgeOptions options)
    {
        var root = "/" + options.RoutePrefix.Trim('/');
        var entries = new List<NavEntry>();

        foreach (var e in bridge.Entities)
        {
            if (e.Nav.Hidden || e.IsJoinEntity || !await bridge.IsAllowedAsync(e, AdminAction.Read)) continue;
            entries.Add(new NavEntry(e.Nav.Label ?? e.Label, $"{root}/entities/{e.RouteName}", Icons.Material.Filled.TableChart, e.Nav));
        }

        foreach (var d in bridge.Dashboards)
        {
            if (d.Nav.Hidden || !await bridge.IsAllowedAsync(d)) continue;
            entries.Add(new NavEntry(d.Nav.Label ?? d.Title, $"{root}/dashboards/{d.RouteName}", Icons.Material.Filled.Dashboard, d.Nav));
        }

        foreach (var f in bridge.Forms)
        {
            if (f.Nav.Hidden || !await bridge.IsAllowedAsync(f)) continue;
            entries.Add(new NavEntry(f.Nav.Label ?? f.Title, $"{root}/forms/{f.RouteName}", Icons.Material.Filled.Description, f.Nav));
        }

        foreach (var p in options.Pages)
        {
            if (p.Nav.Hidden) continue;
            entries.Add(new NavEntry(p.Nav.Label!, PageHref(p), Icons.Material.Filled.Article, p.Nav));
        }

        return entries;
    }

    private static string PageHref(PageMeta meta) =>
        meta.ComponentType.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Select(r => r.Template)
            .First();
}
