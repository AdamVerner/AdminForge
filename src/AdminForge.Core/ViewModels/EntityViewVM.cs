namespace AdminForge.Core.ViewModels;

/// <summary>
/// Read-only view of a single entity instance. Same value-flattening rules as
/// <see cref="EntityListRowVM"/>: navigation references become <see cref="NavigationRef"/>
/// (or lists thereof).
/// </summary>
public sealed class EntityViewVM
{
    public required string EntityName { get; init; }

    public required string Key { get; init; }

    /// <summary>Property name → display-ready value, in declared column order.</summary>
    public required IReadOnlyDictionary<string, object?> Values { get; init; }

    /// <summary>
    /// Materialised related-table links (auto-generated per collection nav plus any
    /// explicit cross-entity links). Empty when no related links apply.
    /// </summary>
    public IReadOnlyList<RelatedLinkVM> RelatedLinks { get; init; } = [];
}

/// <summary>
/// Renderer-facing projection of a <see cref="Metadata.RelatedLinkMeta"/>.
/// <see cref="Filter"/> already carries the resolved per-source filter dictionary
/// — the renderer turns it into the <c>?filter:{key}={value}</c> query string.
/// </summary>
public sealed class RelatedLinkVM
{
    public required string Label { get; init; }

    public string? Icon { get; init; }

    /// <summary><see cref="Metadata.EntityMeta.RouteName"/> of the target entity.</summary>
    public required string RouteName { get; init; }

    /// <summary>Resolved filter dictionary applied to the target list page.</summary>
    public required IReadOnlyDictionary<string, object?> Filter { get; init; }
}
