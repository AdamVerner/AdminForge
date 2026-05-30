using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent overrides for an entity / dashboard / form's sidebar nav entry.
/// Mutates the supplied <see cref="NavMeta"/> in place — the builder is just a
/// thin chaining wrapper so call-sites read like the examples in the README.
/// </summary>
public sealed class NavBuilder
{
    private readonly NavMeta _meta;

    internal NavBuilder(NavMeta meta) => _meta = meta;

    /// <summary>Override the sidebar label.</summary>
    public NavBuilder Label(string label)
    {
        _meta.Label = label;
        return this;
    }

    /// <summary>Place this entry under a collapsible group section.</summary>
    public NavBuilder Group(string group)
    {
        _meta.Group = group;
        return this;
    }

    /// <summary>Sort key within the group (lower wins).</summary>
    public NavBuilder Order(int order)
    {
        _meta.Order = order;
        return this;
    }

    /// <summary>Hide the entry from the sidebar (entity still routable directly).</summary>
    public NavBuilder Hidden()
    {
        _meta.Hidden = true;
        return this;
    }
}
