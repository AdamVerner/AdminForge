namespace AdminForge.Core.ViewModels;

/// <summary>
/// One row in an entity list view. Values are flattened — scalar columns
/// hold their raw CLR value (boxed); navigation references are replaced
/// with a <see cref="NavigationRef"/> (single) or
/// <c>IReadOnlyList&lt;NavigationRef&gt;</c> (collection).
/// </summary>
public sealed class EntityListRowVM
{
    /// <summary>String-encoded primary key for the row, suitable for routing.</summary>
    public required string Key { get; init; }

    /// <summary>Column-name → display-ready value map. Navigation values use <see cref="NavigationRef"/>.</summary>
    public required IReadOnlyDictionary<string, object?> Values { get; init; }
}

/// <summary>
/// View model for an entity list page: rows + total count + the query that produced them.
/// </summary>
public sealed class EntityListVM
{
    /// <summary>Logical entity name (matches <c>EntityMeta.Name</c>).</summary>
    public required string EntityName { get; init; }

    public required IReadOnlyList<EntityListRowVM> Rows { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }
}
