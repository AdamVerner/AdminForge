using System.Linq.Expressions;

namespace AdminForge.Core.Metadata;

/// <summary>
/// Describes a "Related" link rendered on an entity view page — either auto-generated
/// from a collection navigation (FK pre-filter) or registered explicitly via
/// <c>RelatedLink&lt;TTarget&gt;(...)</c> on the fluent builder.
/// </summary>
public sealed class RelatedLinkMeta
{
    /// <summary>CLR type of the related entity (target of the link).</summary>
    public required Type RelatedEntityType { get; init; }

    /// <summary>Display label rendered on the button/link (e.g. "View 12 tasks →").</summary>
    public required string Label { get; set; }

    /// <summary>Optional Material icon name; renderer maps to its icon set.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Compiled function producing the query-string filter dictionary from the source
    /// entity instance. Auto-links produce a single FK-equality entry; user-defined
    /// links may produce several.
    /// </summary>
    public required Func<object, IReadOnlyDictionary<string, object?>> FilterBuilder { get; init; }

    /// <summary>
    /// For auto-generated links from a collection navigation: the navigation property name
    /// on the source entity (e.g. <c>"Todos"</c>). Null for cross-entity links registered
    /// via <c>RelatedLink&lt;TTarget&gt;</c>.
    /// </summary>
    public string? SourceNavigationName { get; init; }
}
