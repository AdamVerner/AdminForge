namespace AdminForge;

/// <summary>The sidebar's order, shared by every surface that lists what the panel offers.</summary>
internal static class NavGrouping
{
    public sealed record Group<T>(string? Key, IReadOnlyList<T> Entries);

    /// <summary>
    /// Main entries first: the ungrouped ones, then each group, in registration order. Then one
    /// group each for <see cref="NavArea.Settings"/> and <see cref="NavArea.Account"/>.
    /// </summary>
    public static IReadOnlyList<Group<T>> Arrange<T>(IEnumerable<T> entries, Func<T, NavModel> nav)
    {
        var ordered = entries.OrderBy(e => nav(e).Order).ToList();
        var main = ordered.Where(e => nav(e).Area == NavArea.Main).ToList();
        var groups = new List<Group<T>>();
        if (main.Where(e => nav(e).Group is null).ToList() is { Count: > 0 } ungrouped)
            groups.Add(new(null, ungrouped));
        foreach (var g in main.Where(e => nav(e).Group is not null).GroupBy(e => nav(e).Group))
            groups.Add(new(g.Key, g.ToList()));
        foreach (var area in new[] { NavArea.Settings, NavArea.Account })
            if (ordered.Where(e => nav(e).Area == area).ToList() is { Count: > 0 } inArea)
                groups.Add(new(area.ToString(), inArea));
        return groups;
    }
}
