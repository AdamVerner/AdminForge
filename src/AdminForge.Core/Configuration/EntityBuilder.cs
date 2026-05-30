using System.Linq.Expressions;
using System.Reflection;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent surface for tweaking the metadata of one entity registered with
/// <c>AdminForgeBuilder.AddTable&lt;T&gt;</c>. All overrides mutate the wrapped
/// <see cref="EntityMeta"/> in place; the meta is built immutably afterwards.
/// </summary>
public sealed class EntityBuilder<T>
    where T : class
{
    private readonly EntityMeta _meta;
    private readonly Dictionary<string, ColumnMeta> _columnsByName;

    internal EntityBuilder(EntityMeta meta)
    {
        _meta = meta;
        _columnsByName = meta.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);
    }

    /// <summary>Override the entity's display label.</summary>
    public EntityBuilder<T> Label(string label)
    {
        _meta.Label = label;
        return this;
    }

    /// <summary>Customise the sidebar nav entry (label, group, order, hidden).</summary>
    public EntityBuilder<T> Nav(Action<NavBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new NavBuilder(_meta.Nav));
        return this;
    }

    /// <summary>
    /// Tweak a single column (label, helper text, validators, visibility).
    /// </summary>
    public EntityBuilder<T> Column<TProp>(
        Expression<Func<T, TProp>> selector,
        Action<ColumnBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        var propertyName = GetPropertyName(selector);
        if (!_columnsByName.TryGetValue(propertyName, out var column))
        {
            throw new InvalidOperationException(
                $"Column '{propertyName}' was not discovered on entity '{typeof(T).Name}'."
            );
        }

        configure(new ColumnBuilder(column));
        return this;
    }

    /// <summary>
    /// Override the <c>DisplayLabel</c> resolver — the short string shown when this
    /// entity is referenced as a navigation target. Defaults to the heuristic in
    /// <see cref="DisplayLabelResolver"/>.
    /// </summary>
    public EntityBuilder<T> DisplayMember<TProp>(Expression<Func<T, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var compiled = selector.Compile();
        _meta.DisplayLabel = instance =>
        {
            var value = compiled((T)instance);
            return value?.ToString() ?? string.Empty;
        };
        return this;
    }

    private static string GetPropertyName<TProp>(Expression<Func<T, TProp>> selector)
    {
        // Unwrap conversions inserted for value-type → object selectors etc.
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
