using System.Globalization;
using AdminForge.Core.Metadata;

namespace AdminForge.UI.Blazor;

/// <summary>
/// One rendering of a scalar value, so a timestamp reads the same in a list cell as on the
/// detail page. Null is the caller's to render — a cell leaves it blank, a detail page says so.
/// </summary>
public static class ValueFormatter
{
    /// <summary>Default patterns, overridden per column by <c>ColumnBuilder.Format(...)</c>.</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateFormat = "yyyy-MM-dd";

    public static string? Format(object? value, ColumnMeta? column = null)
    {
        if (value is null)
            return null;
        var custom = column?.Format;
        return value switch
        {
            bool b => custom is null ? (b ? "yes" : "no") : b.ToString(),
            DateTime dt => dt.ToString(custom ?? DateTimeFormat, CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(
                custom ?? DateTimeFormat,
                CultureInfo.InvariantCulture
            ),
            DateOnly d => d.ToString(custom ?? DateFormat, CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString(custom ?? "HH:mm", CultureInfo.InvariantCulture),
            IFormattable f when custom is not null => f.ToString(
                custom,
                CultureInfo.InvariantCulture
            ),
            _ => value.ToString(),
        };
    }
}
