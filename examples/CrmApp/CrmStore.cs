using System.Reflection;
using AdminForge.Core.Contracts;

namespace CrmApp;

/// <summary>Seeded data standing in for the services a real host would call.</summary>
public sealed class CrmStore
{
    public IReadOnlyList<Organization> Organizations { get; }
    public IReadOnlyList<Account> Accounts { get; }
    public IReadOnlyList<Member> Members { get; }
    public IReadOnlyList<ApiKey> ApiKeys { get; }

    public CrmStore()
    {
        var start = new DateTime(2025, 1, 6, 9, 30, 0, DateTimeKind.Utc);
        string[] orgNames = ["Northwind", "Acme Industrial", "Globex"];
        Organizations =
        [
            .. orgNames.Select(
                (name, i) =>
                    new Organization(
                        i + 1,
                        name,
                        (Plan)(i % 3),
                        (i + 1) * 25,
                        start.AddDays(i * 31),
                        i == 2 ? "unpaid invoice" : null
                    )
            ),
        ];

        string[] roles = ["Owner", "Admin", "Operator", "Viewer"];
        var accounts = new List<Account>();
        var members = new List<Member>();
        var keys = new List<ApiKey>();
        var accountId = 1;
        foreach (var org in Organizations)
        {
            // Enough rows on the first organization that its embedded table pages.
            var count = org.Id == 1 ? 32 : 6;
            for (var i = 0; i < count; i++, accountId++)
            {
                accounts.Add(
                    new Account(
                        accountId,
                        $"user{accountId}@{org.Name.Split(' ')[0].ToLowerInvariant()}.example",
                        $"{org.Name.Split(' ')[0]} User {i + 1}",
                        start.AddDays(accountId)
                    )
                );
                members.Add(
                    new Member(
                        members.Count + 1,
                        org.Id,
                        accountId,
                        roles[i % roles.Length],
                        start.AddDays(accountId).AddHours(3),
                        i % 7 != 0
                    )
                );
            }
            for (var i = 0; i < 3; i++)
            {
                keys.Add(
                    new ApiKey(
                        keys.Count + 1,
                        org.Id,
                        $"{org.Name.Split(' ')[0].ToLowerInvariant()}-key-{i + 1}",
                        start.AddDays(org.Id * 10 + i),
                        i == 0 ? null : start.AddDays(org.Id * 10 + i + 365),
                        i == 2
                    )
                );
            }
        }
        Accounts = accounts;
        Members = members;
        ApiKeys = keys;
    }
}

/// <summary>
/// Filter, search, sort and page in memory, so the example's providers are the shape a real one
/// is: they translate <see cref="ListQuery"/> onto whatever the service below takes.
/// </summary>
public static class InMemoryQuery
{
    public static ListResult<T> Apply<T>(
        IEnumerable<T> rows,
        ListQuery query,
        bool searchable = true
    )
    {
        foreach (var (name, wanted) in query.Filters)
        {
            var text = wanted?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            rows = rows.Where(r => Matches(Read(r, name), text));
        }

        if (searchable && !string.IsNullOrWhiteSpace(query.Search))
        {
            var needle = query.Search.Trim();
            rows = rows.Where(r =>
                typeof(T)
                    .GetProperties()
                    .Any(p =>
                        p.PropertyType == typeof(string)
                        && p.GetValue(r) is string s
                        && s.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    )
            );
        }

        if (query.SortBy is { Length: > 0 } sortBy)
        {
            rows = query.SortDescending
                ? rows.OrderByDescending(r => Read(r, sortBy), NullSafe.Comparer)
                : rows.OrderBy(r => Read(r, sortBy), NullSafe.Comparer);
        }

        var all = rows.ToList();
        return new ListResult<T>
        {
            Items = all.Skip(query.Page * query.PageSize).Take(query.PageSize).ToList(),
            TotalCount = all.Count,
        };
    }

    private static object? Read<T>(T row, string property) =>
        typeof(T).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(row);

    // A string column matches on substring; anything else on its rendered value, which is what
    // the panel's filter box and a related link's query string both send.
    private static bool Matches(object? value, string wanted) =>
        value is string s
            ? s.Contains(wanted, StringComparison.OrdinalIgnoreCase)
            : string.Equals(value?.ToString(), wanted, StringComparison.OrdinalIgnoreCase);

    private sealed class NullSafe : IComparer<object?>
    {
        public static readonly IComparer<object?> Comparer = new NullSafe();

        public int Compare(object? x, object? y) =>
            (x, y) switch
            {
                (null, null) => 0,
                (null, _) => -1,
                (_, null) => 1,
                (IComparable a, _) => a.CompareTo(y),
                _ => 0,
            };
    }
}
