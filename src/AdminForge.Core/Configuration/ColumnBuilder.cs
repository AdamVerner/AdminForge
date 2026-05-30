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
}
