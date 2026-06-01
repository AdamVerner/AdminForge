using System.Linq.Expressions;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for a column added via <see cref="EntityBuilder{T}.AddColumn{TValue}"/>.
/// Wraps the freshly-minted <see cref="ColumnMeta"/> so the caller can label it, mark it
/// sortable/filterable, and (required) supply the value selector via <see cref="From"/>.
/// </summary>
public sealed class CustomColumnBuilder<T, TValue>
    where T : class
{
    private readonly ColumnMeta _meta;
    internal Expression<Func<T, TValue>>? Selector { get; private set; }

    internal CustomColumnBuilder(ColumnMeta meta) => _meta = meta;

    /// <summary>Override the column label (defaults to the column name).</summary>
    public CustomColumnBuilder<T, TValue> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _meta.Label = label;
        return this;
    }

    /// <summary>Helper text shown alongside the column (currently used by future tooltips).</summary>
    public CustomColumnBuilder<T, TValue> Description(string description)
    {
        _meta.Description = description;
        return this;
    }

    /// <summary>
    /// Required: supply the server-side projection that computes the value. The
    /// expression is translated by the data provider; it must therefore be EF-translatable
    /// (no client-side method calls).
    /// </summary>
    public CustomColumnBuilder<T, TValue> From(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = selector;
        return this;
    }

    /// <summary>Opt the column into <see cref="Contracts.ListQuery.SortBy"/>.</summary>
    public CustomColumnBuilder<T, TValue> Sortable(bool sortable = true)
    {
        _meta.IsSortable = sortable;
        return this;
    }

    /// <summary>Opt the column into <see cref="Contracts.ListQuery.Filters"/> (exact-match).</summary>
    public CustomColumnBuilder<T, TValue> Filterable(bool filterable = true)
    {
        _meta.IsFilterable = filterable;
        return this;
    }

    /// <summary>Hide the column from list views (still computed if referenced elsewhere).</summary>
    public CustomColumnBuilder<T, TValue> HiddenInList()
    {
        _meta.ShowInList = false;
        return this;
    }
}
