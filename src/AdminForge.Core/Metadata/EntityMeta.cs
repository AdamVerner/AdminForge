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

    /// <summary>
    /// Columns in declaration order. Backed by a concrete <see cref="List{T}"/> so the fluent
    /// builder can <c>HideColumn</c> / <c>AddColumn</c>; treat as read-only outside of the
    /// configuration phase.
    /// </summary>
    public required List<ColumnMeta> Columns { get; init; }

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

    /// <summary>
    /// "Related" link descriptors rendered on the entity view between scalars and actions.
    /// Populated by <see cref="AdminForgeBuilder.AddTable{T}"/>: one auto-link per collection
    /// navigation (unless suppressed via <c>HideRelatedLink</c>), plus any explicit cross-entity
    /// links registered via <c>RelatedLink&lt;TTarget&gt;</c>.
    /// </summary>
    public List<RelatedLinkMeta> RelatedLinks { get; init; } = [];

    /// <summary>
    /// User-registered custom actions surfaced on the entity view page. Empty when no
    /// <c>AddAction</c> calls were made.
    /// </summary>
    public List<ActionMeta> Actions { get; init; } = [];

    /// <summary>
    /// Collection-navigation property names suppressed from auto-link rendering via
    /// <c>EntityBuilder.HideRelatedLink(...)</c>. The renderer skips matching entries
    /// when materialising auto-links on the entity view page.
    /// </summary>
    public HashSet<string> HiddenRelatedNavigations { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Optional live-polling interval registered via <c>EntityBuilder.WithLivePolling</c>.
    /// When set, the entity <em>view</em> page re-fetches the displayed row every interval
    /// (the entity list table is NOT polled — that scope was dropped after the initial
    /// Phase 5 build).
    /// </summary>
    public TimeSpan? LivePollingInterval { get; set; }
}
