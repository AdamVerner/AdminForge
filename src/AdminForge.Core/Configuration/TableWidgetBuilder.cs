using System.Linq.Expressions;
using System.Reflection;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for a table widget embedded in a dashboard. Read-only — for
/// editing the user navigates to the entity's standard list/edit pages.
/// </summary>
public sealed class TableWidgetBuilder<TEntity>
    where TEntity : class
{
    private readonly List<string> _visibleColumns = [];
    private int? _maxRows;
    private string? _sortBy;
    private bool _sortDescending;

    /// <summary>Title displayed above the widget. Defaults to the entity's CLR name.</summary>
    public string? Title { get; private set; }

    /// <summary>Override the widget title.</summary>
    public TableWidgetBuilder<TEntity> WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        return this;
    }

    /// <summary>
    /// Project the row to a fixed list of columns (in display order). When omitted
    /// the widget inherits the entity's default visible columns.
    /// </summary>
    public TableWidgetBuilder<TEntity> WithColumns(
        params Expression<Func<TEntity, object?>>[] selectors
    )
    {
        ArgumentNullException.ThrowIfNull(selectors);
        foreach (var s in selectors)
            _visibleColumns.Add(GetPropertyName(s));
        return this;
    }

    /// <summary>Limit the number of rows fetched.</summary>
    public TableWidgetBuilder<TEntity> Take(int maxRows)
    {
        if (maxRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRows), "Must be positive.");
        _maxRows = maxRows;
        return this;
    }

    /// <summary>Sort the widget's rows by the supplied property.</summary>
    public TableWidgetBuilder<TEntity> OrderBy<TProp>(
        Expression<Func<TEntity, TProp>> selector,
        bool descending = false
    )
    {
        ArgumentNullException.ThrowIfNull(selector);
        _sortBy = GetPropertyName(selector);
        _sortDescending = descending;
        return this;
    }

    internal TableWidgetMeta Build(string id) =>
        new()
        {
            Id = id,
            Title = Title ?? typeof(TEntity).Name,
            EntityType = typeof(TEntity),
            VisibleColumns = _visibleColumns.Count == 0 ? null : _visibleColumns.AsReadOnly(),
            MaxRows = _maxRows,
            SortBy = _sortBy,
            SortDescending = _sortDescending,
        };

    private static string GetPropertyName<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        Expression body = selector.Body;
        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            body = unary.Operand;
        if (body is MemberExpression member && member.Member is PropertyInfo property)
            return property.Name;
        throw new ArgumentException(
            $"Expected a simple property access expression, got: {selector}",
            nameof(selector)
        );
    }
}
