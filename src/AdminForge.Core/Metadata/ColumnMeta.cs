using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace AdminForge.Core.Metadata;

/// <summary>
/// Describes a single column/property on an entity as seen by AdminForge.
/// Pure POCO — no behavior. Produced by the reflection scanner, then
/// optionally augmented by the fluent builder.
/// </summary>
public sealed class ColumnMeta
{
    /// <summary>Property name on the CLR type (e.g. "Title").</summary>
    public required string PropertyName { get; init; }

    /// <summary>Display label. Defaults to <see cref="PropertyName"/> but can be overridden.</summary>
    public required string Label { get; set; }

    /// <summary>CLR type of the property (e.g. <c>string</c>, <c>int?</c>, <c>TodoStatus</c>).</summary>
    public required Type ClrType { get; init; }

    /// <summary>Whether the underlying CLR type is nullable (reference or <see cref="Nullable{T}"/>).</summary>
    public required bool IsNullable { get; init; }

    /// <summary>Classification used for rendering and editing.</summary>
    public required ColumnKind Kind { get; init; }

    /// <summary>True if this column is part of the entity's primary key.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>True if this column is a foreign-key scalar (its value points at another entity's key).</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>
    /// When <see cref="IsForeignKey"/> is true, the name of the navigation property
    /// that owns this FK (e.g. FK <c>AssigneeId</c> → nav <c>Assignee</c>).
    /// </summary>
    public string? ForeignKeyNavigation { get; init; }

    /// <summary>
    /// When <see cref="Kind"/> is <see cref="ColumnKind.NavigationReference"/> or
    /// <see cref="ColumnKind.NavigationCollection"/>, the CLR type of the related entity.
    /// </summary>
    public Type? RelatedEntityType { get; init; }

    /// <summary>
    /// When <see cref="Kind"/> is <see cref="ColumnKind.Enum"/>, the underlying enum CLR type
    /// (peeled out of <see cref="Nullable{T}"/> if applicable).
    /// </summary>
    public Type? EnumType { get; init; }

    /// <summary>True if the column was marked database-generated (identity, computed, etc.).</summary>
    public bool IsGenerated { get; init; }

    /// <summary>Maximum string length, if known (from <see cref="MaxLengthAttribute"/> or EF model).</summary>
    public int? MaxLength { get; init; }

    /// <summary>True if the property is required (per <see cref="RequiredAttribute"/> or non-nullable schema).</summary>
    public bool IsRequired { get; init; }

    /// <summary>Optional description / helper text rendered alongside the field.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// True when this column appears in list (table) views and the filter bar.
    /// <para>
    /// Default semantics — <b>list views are opt-in</b>: auto-discovered scalar
    /// columns default to <c>false</c>; the host opts each desired column in via
    /// <c>EntityBuilder&lt;T&gt;.AddColumn(selector, ...)</c>. Custom computed
    /// columns added via <c>AddColumn&lt;TValue&gt;(name, ...)</c> default to <c>true</c>.
    /// </para>
    /// <para>
    /// <c>HideColumn(selector)</c> flips this back to <c>false</c> (and also sets
    /// <see cref="HiddenInEdit"/>). Entity view + edit visibility is governed by
    /// <see cref="HiddenInEdit"/> and is opt-out by default.
    /// </para>
    /// </summary>
    public bool ShowInList { get; set; }

    /// <summary>
    /// Hide this column from edit forms. Entity view/edit surfaces show all
    /// auto-discovered columns by default (opt-out); set this to true to suppress one.
    /// </summary>
    public bool HiddenInEdit { get; set; }

    /// <summary>
    /// User-supplied validators. Each returns null on success or an error message on failure.
    /// </summary>
    public List<ColumnValidator> Validators { get; } = [];

    /// <summary>
    /// True if this column was added via <c>AddColumn&lt;TValue&gt;</c> on the fluent builder
    /// (i.e. it does not correspond to a real CLR property — the value is computed by
    /// projecting <see cref="CustomValueSelector"/> through the underlying provider).
    /// </summary>
    public bool IsCustom { get; init; }

    /// <summary>
    /// For custom columns: the user-provided <c>Expression&lt;Func&lt;TEntity, TValue&gt;&gt;</c>
    /// stored as a <see cref="LambdaExpression"/> so the data provider can translate it
    /// into a server-side projection.
    /// </summary>
    public LambdaExpression? CustomValueSelector { get; init; }

    /// <summary>True if this column participates in <c>ListQuery.SortBy</c>. Default true for scalars; opt-in for custom columns.</summary>
    public bool IsSortable { get; set; } = true;

    /// <summary>True if this column participates in <c>ListQuery.Filters</c>. Default true for scalars; opt-in for custom columns.</summary>
    public bool IsFilterable { get; set; } = true;

    /// <summary>
    /// For navigation-reference columns: optional override producing the link text from
    /// the related instance. Set via <c>ColumnBuilder.LinkText(...)</c>. When null the
    /// renderer falls back to the related entity's <c>DisplayLabel</c>.
    /// </summary>
    public Func<object, string>? LinkTextResolver { get; set; }

    /// <summary>
    /// Stored <c>Expression&lt;Func&lt;TTarget, string&gt;&gt;</c> from
    /// <see cref="Configuration.ColumnBuilder.LinkText"/>. Kept so the renderer can
    /// reason about (or compile on-demand) the user's intent. The compiled resolver
    /// is mirrored on <see cref="LinkTextResolver"/>.
    /// </summary>
    public LambdaExpression? LinkTextExpression { get; set; }
}

/// <summary>
/// A single user-registered validator for a column. Receives the candidate value
/// and returns null on success or a human-readable error message on failure.
/// </summary>
/// <param name="Validate">Predicate returning null when valid, otherwise an error message.</param>
public sealed record ColumnValidator(Func<object?, string?> Validate);
