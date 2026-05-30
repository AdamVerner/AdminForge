namespace AdminForge.Core.ViewModels;

/// <summary>
/// Editable view of a single entity. Edited values are surfaced back to the
/// data provider as a property-name → raw-value dictionary. Navigation references
/// are represented by their <see cref="NavigationRef.Key"/> only (selection happens
/// in the UI; the persistence layer rehydrates the FK).
/// </summary>
public sealed class EntityEditVM
{
    public required string EntityName { get; init; }

    /// <summary>Null when creating a new entity, non-null when editing an existing one.</summary>
    public string? Key { get; init; }

    /// <summary>Current field values, keyed by property name.</summary>
    public required IDictionary<string, object?> Values { get; init; }
}
