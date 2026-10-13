using System.Globalization;

namespace AdminForge.UI;

/// <summary>
/// One rendering of a value, so a timestamp reads the same in a list cell as on the detail page.
/// Null is the caller's to render: a cell leaves it blank, a detail page says so.
/// </summary>
internal static class ValueFormatter
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The column's format, else the panel's format for the value's type, else the default.</summary>
    /// <remarks>A number groups its thousands unless it identifies a row: a key, a link, or a column named like a foreign key.</remarks>
    public static string? Format(
        object? value,
        ColumnModel? column = null,
        PanelModel? panel = null
    )
    {
        if (value is null)
            return null;
        var custom = column?.Format ?? panel?.Formats.GetValueOrDefault(value.GetType());
        var grouped =
            custom is null
            && column is not ({ IsKey: true } or { LinksTo: not null })
            && column?.Name.EndsWith("Id", StringComparison.Ordinal) != true;
        return value switch
        {
            bool b => custom is null ? (b ? "Yes" : "No") : b.ToString(),
            Enum e when custom is null => Members.Humanize(e.ToString()),
            DateTime dt => dt.ToString(custom ?? DateTimeFormat, CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(
                custom ?? DateTimeFormat,
                CultureInfo.InvariantCulture
            ),
            DateOnly d => d.ToString(custom ?? DateFormat, CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString(custom ?? "HH:mm", CultureInfo.InvariantCulture),
            // Four decimals would print a tiny reading as "0" or "-0".
            double or float when custom is null => ((IFormattable)value).ToString(
                Math.Abs(Convert.ToDouble(value, CultureInfo.InvariantCulture)) is > 0 and < 1e-4
                    ? "G4"
                    : grouped
                        ? "#,0.####"
                        : "0.####",
                CultureInfo.InvariantCulture
            ),
            decimal m when grouped => m.ToString("N" + m.Scale, CultureInfo.InvariantCulture),
            IFormattable f when grouped && ScalarTypes.IsInteger(value.GetType()) => f.ToString(
                "#,0",
                CultureInfo.InvariantCulture
            ),
            IFormattable f when custom is not null => f.ToString(
                custom,
                CultureInfo.InvariantCulture
            ),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
