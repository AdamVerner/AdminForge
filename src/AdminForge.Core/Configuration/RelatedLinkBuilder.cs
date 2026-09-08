using System.Linq.Expressions;
using System.Reflection;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for tweaking a single <see cref="RelatedLinkMeta"/>, either an
/// auto-generated one (via <c>EntityBuilder.RelatedLink(navSelector, ...)</c>) or
/// a freshly registered cross-entity link (via <c>RelatedLink&lt;TTarget&gt;</c>).
/// </summary>
public class RelatedLinkBuilder
{
    private protected readonly RelatedLinkMeta Meta;

    internal RelatedLinkBuilder(RelatedLinkMeta meta) => Meta = meta;

    /// <summary>Override the link's display label.</summary>
    public RelatedLinkBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Meta.Label = label;
        return this;
    }

    /// <summary>Set an icon name (string token; renderer maps to its icon set).</summary>
    public RelatedLinkBuilder Icon(string icon)
    {
        Meta.Icon = icon;
        return this;
    }

    /// <summary>
    /// Show the target's table on the source's detail page, filtered to this link, instead of
    /// a button that navigates to it.
    /// </summary>
    public RelatedLinkBuilder Inline()
    {
        Meta.Inline = true;
        return this;
    }
}

/// <summary>
/// <see cref="RelatedLinkBuilder"/> for a link whose target type is known at compile time, so
/// an inline table's columns can be named with selectors.
/// </summary>
public sealed class RelatedLinkBuilder<TTarget> : RelatedLinkBuilder
{
    internal RelatedLinkBuilder(RelatedLinkMeta meta)
        : base(meta) { }

    /// <summary>Override the link's display label.</summary>
    public new RelatedLinkBuilder<TTarget> Label(string label)
    {
        base.Label(label);
        return this;
    }

    /// <summary>Set an icon name (string token; renderer maps to its icon set).</summary>
    public new RelatedLinkBuilder<TTarget> Icon(string icon)
    {
        base.Icon(icon);
        return this;
    }

    /// <inheritdoc cref="RelatedLinkBuilder.Inline()" />
    public new RelatedLinkBuilder<TTarget> Inline()
    {
        base.Inline();
        return this;
    }

    /// <summary>
    /// Show these columns, in this order, in the inline table — a subset of what the target
    /// table lists on its own page, which is what shows when this is not called.
    /// </summary>
    public RelatedLinkBuilder<TTarget> Columns(
        params Expression<Func<TTarget, object?>>[] selectors
    )
    {
        ArgumentNullException.ThrowIfNull(selectors);
        if (selectors.Length == 0)
            throw new ArgumentException("Name at least one column.", nameof(selectors));
        Meta.Columns = selectors.Select(PropertyName).ToList();
        return this;
    }

    private static string PropertyName(Expression<Func<TTarget, object?>> selector)
    {
        Expression body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            body = convert.Operand;
        if (body is MemberExpression { Member: PropertyInfo property })
            return property.Name;
        throw new ArgumentException(
            $"Expected a simple property access expression, got: {selector}",
            nameof(selector)
        );
    }
}
