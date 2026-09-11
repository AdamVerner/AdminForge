using System.Linq.Expressions;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for a computed column added via <see cref="EntityBuilder{T}.Column{TValue}(string, Action{CustomColumnBuilder{T, TValue}})"/>.
/// Exactly one value source is required: <see cref="From"/> projects server-side, <see cref="Resolve"/>
/// computes in-process.
/// </summary>
public sealed class CustomColumnBuilder<T, TValue>
    where T : class
{
    private readonly ColumnMeta _meta;

    internal Expression<Func<T, TValue>>? Selector { get; private set; }
    internal Func<IServiceProvider, object, CancellationToken, Task<object?>>? Resolver
    {
        get;
        private set;
    }

    /// <summary>True once the caller named a list visibility, so the default no longer applies.</summary>
    internal bool ListVisibilitySet { get; private set; }

    internal CustomColumnBuilder(ColumnMeta meta) => _meta = meta;

    /// <summary>Override the column label (defaults to the column name).</summary>
    public CustomColumnBuilder<T, TValue> Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _meta.Label = label;
        return this;
    }

    /// <summary>Helper text shown alongside the column.</summary>
    public CustomColumnBuilder<T, TValue> Description(string description)
    {
        _meta.Description = description;
        return this;
    }

    /// <summary>
    /// Compute the value as a server-side projection. The expression is translated by the data
    /// provider, so it must be EF-translatable — which is also why it can be sorted and filtered
    /// on. Rendering it on the detail page needs the provider to implement
    /// <c>IAdminColumnProjector&lt;T&gt;</c>; the EF Core provider does.
    /// </summary>
    public CustomColumnBuilder<T, TValue> From(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Selector = selector;
        return this;
    }

    /// <summary>
    /// Compute the value in-process from the materialised instance — a call to another service,
    /// another database, an expensive calculation. Runs once per rendered instance, so the column
    /// is detail-only unless <see cref="ShownInList"/> opts it into the table as well. Cannot be
    /// sorted or filtered: the database has never seen the value.
    /// </summary>
    public CustomColumnBuilder<T, TValue> Resolve(
        Func<IServiceProvider, T, CancellationToken, Task<TValue>> resolver
    )
    {
        ArgumentNullException.ThrowIfNull(resolver);
        Resolver = async (services, instance, ct) =>
            await resolver(services, (T)instance, ct).ConfigureAwait(false);
        return this;
    }

    /// <summary>Opt the column into <see cref="Contracts.ListQuery.SortBy"/>. Projected columns only.</summary>
    public CustomColumnBuilder<T, TValue> Sortable(bool sortable = true)
    {
        _meta.IsSortable = sortable;
        return this;
    }

    /// <summary>Opt the column into <see cref="Contracts.ListQuery.Filters"/> (exact-match). Projected columns only.</summary>
    public CustomColumnBuilder<T, TValue> Filterable(bool filterable = true)
    {
        _meta.IsFilterable = filterable;
        return this;
    }

    /// <summary>Format string handed to the value when rendering — a date pattern, a numeric format.</summary>
    public CustomColumnBuilder<T, TValue> Format(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        _meta.Format = format;
        return this;
    }

    /// <summary>Render the value as a link to <typeparamref name="TTarget"/>'s detail page, keyed by the value.</summary>
    public CustomColumnBuilder<T, TValue> LinksTo<TTarget>()
        where TTarget : class
    {
        _meta.LinkTargetType = typeof(TTarget);
        return this;
    }

    /// <summary>Show a <see cref="Resolve"/>d column in the table too — one call per row on every page render.</summary>
    public CustomColumnBuilder<T, TValue> ShownInList()
    {
        _meta.ShowInList = true;
        ListVisibilitySet = true;
        return this;
    }

    /// <summary>Hide the column from list views.</summary>
    public CustomColumnBuilder<T, TValue> HiddenInList()
    {
        _meta.ShowInList = false;
        ListVisibilitySet = true;
        return this;
    }

    /// <summary>Hide the column from the entity view page.</summary>
    public CustomColumnBuilder<T, TValue> HiddenInView()
    {
        _meta.HiddenInView = true;
        return this;
    }
}
