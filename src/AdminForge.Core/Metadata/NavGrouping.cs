namespace AdminForge.Core.Metadata;

/// <summary>The sidebar's order, shared by every surface that lists what the panel offers.</summary>
public static class NavGrouping
{
    public sealed record Group<T>(string? Key, IReadOnlyList<T> Entries);

    /// <summary>
    /// Main entries first: ungrouped, then groups by name. Then one group each for
    /// <see cref="NavArea.Settings"/> and <see cref="NavArea.Account"/>, whatever their own group.
    /// Inside a group by <see cref="NavMeta.Order"/>, then label.
    /// </summary>
    public static IReadOnlyList<Group<T>> Arrange<T>(
        IEnumerable<T> entries,
        Func<T, NavMeta> nav,
        Func<T, string> label
    ) =>
        entries
            .GroupBy(e =>
                (
                    nav(e).Area,
                    Key: nav(e).Area == NavArea.Main ? nav(e).Group : nav(e).Area.ToString()
                )
            )
            .OrderBy(g => g.Key.Area)
            .ThenBy(g => g.Key.Key is null ? 0 : 1)
            .ThenBy(g => g.Key.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Group<T>(
                g.Key.Key,
                g.OrderBy(e => nav(e).Order ?? int.MaxValue)
                    .ThenBy(label, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            ))
            .ToList();
}
