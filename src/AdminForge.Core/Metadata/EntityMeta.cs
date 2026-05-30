namespace AdminForge.Core.Metadata;

/// <summary>
/// Describes an entity (CLR type backed by a <c>DbSet</c>) as seen by AdminForge.
/// Pure POCO — produced by the reflection scanner and possibly mutated by the
/// fluent builder before being handed to the renderer.
/// </summary>
public sealed class EntityMeta
{
    /// <summary>CLR type of the entity.</summary>
    public required Type ClrType { get; init; }

    /// <summary>Short class name, used as the default label and policy key.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe name used when this entity appears as a route segment.
    /// Defaults to <see cref="Name"/> but may be overridden via the fluent builder.
    /// </summary>
    public string RouteName { get; set; } = string.Empty;

    /// <summary>Display label (defaults to <see cref="Name"/>, overridable via builder).</summary>
    public required string Label { get; set; }

    /// <summary>Columns in declaration order.</summary>
    public required IReadOnlyList<ColumnMeta> Columns { get; init; }

    /// <summary>
    /// Names of the primary-key columns, in EF's declared order. Empty for keyless entities
    /// (which are skipped by default). Composite keys are supported (multiple entries).
    /// </summary>
    public required IReadOnlyList<string> PrimaryKeyPropertyNames { get; init; }

    /// <summary>
    /// True if this is an EF join entity for an implicit many-to-many relationship.
    /// Hidden from the default nav scaffolding.
    /// </summary>
    public bool IsJoinEntity { get; init; }

    /// <summary>Navigation entry describing where this entity appears in the sidebar.</summary>
    public NavMeta Nav { get; set; } = new();

    /// <summary>
    /// Optional resolver returning a short human-readable label for an instance, used when
    /// this entity is referenced as a navigation target. Configured via the fluent builder.
    /// </summary>
    public Func<object, string>? DisplayLabel { get; set; }
}
