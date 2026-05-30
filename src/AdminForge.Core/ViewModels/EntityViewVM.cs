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
}
