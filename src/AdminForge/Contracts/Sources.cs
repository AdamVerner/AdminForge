using System.Linq.Expressions;

namespace AdminForge;

/// <summary>Lists a table's rows. A table whose source only lists has no detail page.</summary>
public interface IAdminSource<T>
    where T : class
{
    Task<AdminPage<T>> ListAsync(AdminQuery<T> query, CancellationToken ct);
}

/// <summary>Lists a table's rows and finds one by key: the key property's type, or <c>object?[]</c> for a composite key.</summary>
public interface IAdminSource<T, in TKey> : IAdminSource<T>
    where T : class
{
    Task<T?> FindAsync(TKey key, CancellationToken ct);
}

/// <summary>A source that implements this gets create and edit.</summary>
public interface IAdminWritable<T>
    where T : class
{
    /// <summary>Stores a new row and returns it as stored, with its generated key.</summary>
    Task<T> CreateAsync(T row, CancellationToken ct);

    /// <summary>Stores <paramref name="row"/>; <paramref name="changed"/> names the properties whose value differs from the stored row.</summary>
    Task<T> UpdateAsync(T row, IReadOnlySet<string> changed, CancellationToken ct);
}

/// <summary>A source that implements this gets delete.</summary>
public interface IAdminDeletable<T>
    where T : class
{
    Task DeleteAsync(T row, CancellationToken ct);
}

/// <summary>One page of rows and the number of rows across every page.</summary>
public sealed record AdminPage<T>(IReadOnlyList<T> Rows, int Total);

/// <summary>Builds pages from rows already in memory.</summary>
public static class AdminPage
{
    /// <summary>Applies the query's filters (text contains unless <see cref="AdminQuery{T}.Exact"/>, anything else equals), its sort and its page. Search is the caller's.</summary>
    public static AdminPage<T> From<T>(IEnumerable<T> rows, AdminQuery<T> query)
        where T : class
    {
        foreach (var (name, wanted) in query.Filters)
        {
            var property = typeof(T).GetProperty(name)!;
            rows = rows.Where(r =>
                property.GetValue(r) is var value
                && wanted is string text
                && value is string s
                && !query.Exact.Contains(name)
                    ? s.Contains(text, StringComparison.OrdinalIgnoreCase)
                    : Equals(value, wanted)
            );
        }
        if (query.SortBy is { } sortBy)
        {
            var property = typeof(T).GetProperty(sortBy)!;
            rows = query.SortDescending
                ? rows.OrderByDescending(property.GetValue, Comparer<object?>.Default)
                : rows.OrderBy(property.GetValue, Comparer<object?>.Default);
        }
        var all = rows.ToList();
        return new(all.Skip(query.Page.Skip).Take(query.Page.Size).ToList(), all.Count);
    }
}

/// <summary>Which page a list asks for: zero-based <paramref name="Index"/>, <paramref name="Size"/> rows.</summary>
public readonly record struct AdminPageRequest(int Index, int Size)
{
    public int Skip => Index * Size;
}

/// <summary>What a list asks of its source. Filter values arrive converted to the column's type.</summary>
public sealed class AdminQuery<T>(
    AdminPageRequest page,
    string? search = null,
    string? sortBy = null,
    bool sortDescending = false,
    IReadOnlyDictionary<string, object?>? filters = null,
    IReadOnlySet<string>? exact = null,
    IReadOnlySet<string>? hidden = null
)
    where T : class
{
    public AdminPageRequest Page { get; } = page;

    /// <summary>Free text; null when the table is not searchable or nothing was typed.</summary>
    public string? Search { get; } = search;

    /// <summary>The sorted property; null keeps the source's own order.</summary>
    public string? SortBy { get; } = sortBy;
    public bool SortDescending { get; } = sortDescending;

    /// <summary>Filter values by property name.</summary>
    public IReadOnlyDictionary<string, object?> Filters { get; } =
        filters ?? new Dictionary<string, object?>();

    /// <summary>Filters that match by equality even on text: a related table's pin to its parent, and link columns.</summary>
    public IReadOnlySet<string> Exact { get; } = exact ?? new HashSet<string>();

    /// <summary>Properties hidden from every surface; a search must not match on them either.</summary>
    public IReadOnlySet<string> Hidden { get; } = hidden ?? new HashSet<string>();

    /// <summary>Whether the list is sorted by <paramref name="column"/>, and in which direction.</summary>
    public bool SortedBy<TProp>(Expression<Func<T, TProp>> column, out bool descending)
    {
        descending = SortDescending;
        return SortBy == Members.Name(column);
    }

    // Three overloads so a value-type column comes back nullable: the unused parameter only
    // makes the signatures differ, the constraints pick the one that applies.
    public TProp? Filter<TProp>(Expression<Func<T, TProp>> column, object? _ = null)
        where TProp : class? => Filters.GetValueOrDefault(Members.Name(column)) as TProp;

    public TProp? Filter<TProp>(Expression<Func<T, TProp>> column, string? _ = null)
        where TProp : struct =>
        Filters.GetValueOrDefault(Members.Name(column)) is TProp value ? value : null;

    public TProp? Filter<TProp>(Expression<Func<T, TProp?>> column, int? _ = null)
        where TProp : struct =>
        Filters.GetValueOrDefault(Members.Name(column)) is TProp value ? value : null;
}
