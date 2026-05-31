using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for tweaking a single <see cref="RelatedLinkMeta"/>, either an
/// auto-generated one (via <c>EntityBuilder.RelatedLink(navSelector, ...)</c>) or
/// a freshly registered cross-entity link (via <c>RelatedLink&lt;TTarget&gt;</c>).
/// </summary>
public sealed class RelatedLinkBuilder
{
    private readonly RelatedLinkMeta _meta;

    internal RelatedLinkBuilder(RelatedLinkMeta meta) => _meta = meta;

    /// <summary>Override the link's display label.</summary>
    public RelatedLinkBuilder Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _meta.Label = label;
        return this;
    }

    /// <summary>Set an icon name (string token; renderer maps to its icon set).</summary>
    public RelatedLinkBuilder Icon(string icon)
    {
        _meta.Icon = icon;
        return this;
    }
}
