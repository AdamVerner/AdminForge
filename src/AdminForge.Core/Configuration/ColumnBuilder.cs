using System.Linq.Expressions;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent column override surface. Wraps an existing <see cref="ColumnMeta"/>
/// produced by reflection so the host can tweak label, helper text, visibility,
/// and add custom validators. Generic on <typeparamref name="TProp"/> so the
/// <see cref="LinkText"/> overload can be compile-time-typed against the column's
/// related-entity type.
/// </summary>
public sealed class ColumnBuilder<TProp>
{
    private readonly ColumnMeta _meta;

    internal ColumnBuilder(ColumnMeta meta) => _meta = meta;

    /// <summary>Override the column label shown in lists and forms.</summary>
    public ColumnBuilder<TProp> Label(string label)
    {
        _meta.Label = label;
        return this;
    }

    /// <summary>Set the helper text shown alongside the field in edit forms.</summary>
    public ColumnBuilder<TProp> Description(string description)
    {
        _meta.Description = description;
        return this;
    }

    /// <summary>Hide the column from list views (still editable). Equivalent to clearing <c>ShowInList</c>.</summary>
    public ColumnBuilder<TProp> HiddenInList()
    {
        _meta.ShowInList = false;
        return this;
    }

    /// <summary>Hide the column from edit forms (still visible in lists).</summary>
    public ColumnBuilder<TProp> HiddenInEdit()
    {
        _meta.HiddenInEdit = true;
        return this;
    }

    /// <summary>
    /// Adds a custom validator. <paramref name="predicate"/> returns true for valid values;
    /// when it returns false the supplied <paramref name="message"/> is surfaced.
    /// </summary>
    public ColumnBuilder<TProp> Validate(Func<object?, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _meta.Validators.Add(new ColumnValidator(value => predicate(value) ? null : message));
        return this;
    }

    /// <summary>
    /// For a navigation-reference column: override the link label using a typed
    /// <c>Expression&lt;Func&lt;TProp, string&gt;&gt;</c>. The compile-time parameter
    /// type matches the column's CLR type, so the user no longer casts. A runtime
    /// check enforces that the underlying column is a navigation reference (the
    /// constraint <c>TProp : class</c> doesn't rule out <c>string</c>, which the
    /// scanner reports as <see cref="ColumnKind.Scalar"/>).
    /// </summary>
    public ColumnBuilder<TProp> LinkText(Expression<Func<TProp, string>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (_meta.Kind != ColumnKind.NavigationReference)
        {
            throw new InvalidOperationException(
                $"LinkText is only valid on navigation-reference columns; '{_meta.PropertyName}' is {_meta.Kind}."
            );
        }

        _meta.LinkTextExpression = expression;
        var compiled = expression.Compile();
        _meta.LinkTextResolver = instance =>
            instance is TProp typed ? compiled(typed) ?? string.Empty : string.Empty;
        return this;
    }
}
