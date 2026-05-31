using System.Linq.Expressions;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Fluent column override surface. Wraps an existing <see cref="ColumnMeta"/>
/// produced by reflection so the host can tweak label, helper text, visibility,
/// and add custom validators.
/// </summary>
public sealed class ColumnBuilder
{
    private readonly ColumnMeta _meta;

    internal ColumnBuilder(ColumnMeta meta) => _meta = meta;

    /// <summary>Override the column label shown in lists and forms.</summary>
    public ColumnBuilder Label(string label)
    {
        _meta.Label = label;
        return this;
    }

    /// <summary>Set the helper text shown alongside the field in edit forms.</summary>
    public ColumnBuilder Description(string description)
    {
        _meta.Description = description;
        return this;
    }

    /// <summary>Hide the column from list views (still editable).</summary>
    public ColumnBuilder HiddenInList()
    {
        _meta.HiddenInList = true;
        return this;
    }

    /// <summary>Hide the column from edit forms (still visible in lists).</summary>
    public ColumnBuilder HiddenInEdit()
    {
        _meta.HiddenInEdit = true;
        return this;
    }

    /// <summary>
    /// Adds a custom validator. <paramref name="predicate"/> returns true for valid values;
    /// when it returns false the supplied <paramref name="message"/> is surfaced.
    /// </summary>
    public ColumnBuilder Validate(Func<object?, bool> predicate, string message)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _meta.Validators.Add(new ColumnValidator(value => predicate(value) ? null : message));
        return this;
    }

    /// <summary>
    /// For a navigation-reference column: override the link label using an
    /// <c>Expression&lt;Func&lt;TTarget, string&gt;&gt;</c>. The expression's input parameter
    /// must be assignable from the column's <see cref="ColumnMeta.RelatedEntityType"/>;
    /// validation happens at <see cref="EntityBuilder{T}"/> build time. The expression is
    /// compiled lazily; the compiled delegate is mirrored on
    /// <see cref="ColumnMeta.LinkTextResolver"/>.
    /// </summary>
    /// <remarks>
    /// Non-generic on purpose ("Option A" in the plan): the user passes a typed lambda
    /// (e.g. <c>(User u) =&gt; $"Owned by {u.DisplayName}"</c>) and we validate the target
    /// type at runtime so the parent <c>EntityBuilder&lt;T&gt;</c> doesn't have to take an
    /// extra type parameter per column.
    /// </remarks>
    public ColumnBuilder LinkText(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Parameters.Count != 1)
        {
            throw new ArgumentException(
                "LinkText expression must have exactly one parameter (the related entity).",
                nameof(expression)
            );
        }
        if (expression.ReturnType != typeof(string))
        {
            throw new ArgumentException(
                $"LinkText expression must return string, got {expression.ReturnType}.",
                nameof(expression)
            );
        }
        if (_meta.Kind != ColumnKind.NavigationReference)
        {
            throw new InvalidOperationException(
                $"LinkText is only valid on navigation-reference columns; '{_meta.PropertyName}' is {_meta.Kind}."
            );
        }
        var targetParamType = expression.Parameters[0].Type;
        if (
            _meta.RelatedEntityType is null
            || !targetParamType.IsAssignableFrom(_meta.RelatedEntityType)
        )
        {
            throw new InvalidOperationException(
                $"LinkText expression parameter type '{targetParamType}' is not compatible with "
                    + $"column '{_meta.PropertyName}' related entity type '{_meta.RelatedEntityType}'."
            );
        }

        _meta.LinkTextExpression = expression;
        var compiled = expression.Compile();
        _meta.LinkTextResolver = instance =>
        {
            var result = compiled.DynamicInvoke(instance);
            return result as string ?? string.Empty;
        };
        return this;
    }
}
