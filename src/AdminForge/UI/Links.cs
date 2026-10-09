using System.Globalization;

namespace AdminForge.UI;

/// <summary>Every URL the panel's pages link to, under <see cref="PanelModel.Root"/>.</summary>
internal static class Links
{
    public static string List(string table) => $"{PanelModel.Root}/entities/{table}";

    public static string Row(string table, string key) =>
        $"{List(table)}/{Uri.EscapeDataString(key)}";

    public static string Edit(string table, string key) => $"{Row(table, key)}/edit";

    public static string New(string table) => $"{List(table)}/new";

    /// <summary>The list with <paramref name="filters"/> applied, as a related link opens it.</summary>
    public static string Filtered(string table, IReadOnlyDictionary<string, object?> filters) =>
        $"{List(table)}?"
        + string.Join(
            '&',
            filters.Select(f =>
                $"filter:{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? "")}"
            )
        );

    /// <summary>The table's group and its list, the breadcrumb above any of its pages.</summary>
    public static List<(string, string?)> Crumbs(TableModel table)
    {
        var crumbs = new List<(string, string?)>();
        if (Group(table.Nav) is { } group)
            crumbs.Add((group, null));
        crumbs.Add((table.Title, List(table.Id)));
        return crumbs;
    }

    public static List<(string, string?)>? GroupCrumbs(NavModel nav) =>
        Group(nav) is { } group ? [(group, null)] : null;

    public static string? Group(NavModel nav) =>
        nav.Area == NavArea.Main ? nav.Group : nav.Area.ToString();
}
