using System.Linq.Expressions;

namespace AdminForge.Core.Contracts;

/// <summary>
/// Strongly-typed CRUD surface over a single entity type. The default implementation
/// is EF Core-backed (<c>EfCoreDataProvider&lt;T&gt;</c>) but consumers can register their
/// own implementation per entity to swap out the data layer.
/// </summary>
public interface IAdminDataProvider<T>
    where T : class
{
    /// <summary>
    /// Returns a page of entities matching <paramref name="query"/>. Items are
    /// materialised as plain entity instances; the renderer is responsible for
    /// projecting them into a <c>EntityListVM</c>.
    /// </summary>
    Task<ListResult<T>> ListAsync(ListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Looks up a single entity by its primary key.</summary>
    Task<T?> FindAsync(object?[] keyValues, CancellationToken cancellationToken = default);

    /// <summary>Inserts <paramref name="entity"/> and returns it as persisted (with generated key values populated).</summary>
    Task<T> CreateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Applies updates to the entity matching <paramref name="entity"/>'s key.</summary>
    Task<T> UpdateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Deletes the entity with the supplied key values. Returns false if nothing was found.</summary>
    Task<bool> DeleteAsync(object?[] keyValues, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter/sort/page parameters for <see cref="IAdminDataProvider{T}.ListAsync"/>.
/// Property names are CLR-property names on the entity.
/// </summary>
public sealed class ListQuery
{
    /// <summary>Zero-based page index.</summary>
    public int Page { get; init; }

    /// <summary>Items per page; must be positive.</summary>
    public int PageSize { get; init; } = 25;

    /// <summary>
    /// Optional sort directive. Empty string or null means "no client-specified sort"
    /// (the provider may apply a stable default such as primary key ascending).
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>True if <see cref="SortBy"/> should be sorted descending.</summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// Per-property filters. A string column matches when it contains the value, case-insensitively;
    /// every other column matches exactly, after conversion to the property's CLR type.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Filters { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Free-text search; the provider applies a case-insensitive Contains across
    /// every string scalar column. Null/empty disables the search.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Optional custom-column projections registered via the fluent builder's
    /// <c>AddColumn&lt;TValue&gt;(name, ...)</c>. Each entry maps a column name to its
    /// stored <c>Expression&lt;Func&lt;TEntity, TValue&gt;&gt;</c> selector. The provider
    /// (i) applies the expression directly when the name matches <see cref="SortBy"/> or
    /// a key in <see cref="Filters"/>, and (ii) returns the per-row computed value via
    /// <see cref="ListResult{T}.CustomValues"/>.
    /// </summary>
    public IReadOnlyDictionary<string, CustomColumnSpec> CustomColumns { get; init; } =
        new Dictionary<string, CustomColumnSpec>();
}

/// <summary>
/// One custom-column registration handed to the data provider. Carries the user's
/// stored selector expression and the sort/filter opt-ins.
/// </summary>
/// <param name="Selector">User-provided <c>Expression&lt;Func&lt;TEntity, TValue&gt;&gt;</c>.</param>
/// <param name="Sortable">True if this column can appear in <see cref="ListQuery.SortBy"/>.</param>
/// <param name="Filterable">True if this column can appear in <see cref="ListQuery.Filters"/>.</param>
public sealed record CustomColumnSpec(LambdaExpression Selector, bool Sortable, bool Filterable);

/// <summary>
/// One page of results plus the total count (across all pages) for the same filters.
/// </summary>
public sealed class ListResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }

    /// <summary>
    /// Per-row computed values for any custom columns supplied via
    /// <see cref="ListQuery.CustomColumns"/>. Outer index matches <see cref="Items"/>;
    /// inner dictionary keys are the column names. Empty when no custom columns were requested.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> CustomValues { get; init; } =
        Array.Empty<IReadOnlyDictionary<string, object?>>();
}
