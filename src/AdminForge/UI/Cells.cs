using System.Collections;
using System.Globalization;
using AdminForge.UI.Components;
using Microsoft.AspNetCore.Components;

namespace AdminForge.UI;

/// <summary>One column value wherever it shows: a list cell, a detail value or a widget cell.</summary>
internal static class Cells
{
    /// <param name="link">The linked row's label, for a link column.</param>
    /// <param name="rowHref">Set on a row's first cell: plain text becomes the row's link, anything else gets a hidden one.</param>
    public static RenderFragment Render(
        object? value,
        ColumnModel column,
        PanelModel panel,
        string? link = null,
        string? rowHref = null,
        string? rowLabel = null
    ) =>
        b =>
        {
            if (Plain(value, column) && rowHref is not null)
            {
                b.OpenElement(0, "a");
                b.AddAttribute(1, "class", "af-rowlink");
                b.AddAttribute(2, "href", rowHref);
                b.AddContent(3, value is null ? "—" : Text(value, column, panel));
                b.CloseElement();
                return;
            }
            b.AddContent(4, Value(value, column, panel, link));
            if (rowHref is not null)
            {
                b.OpenElement(5, "a");
                b.AddAttribute(6, "class", "af-rowlink af-sr");
                b.AddAttribute(7, "href", rowHref);
                b.AddContent(8, $"Open {rowLabel}");
                b.CloseElement();
            }
        };

    private static RenderFragment Value(
        object? value,
        ColumnModel column,
        PanelModel panel,
        string? link
    ) =>
        b =>
        {
            if (value is null)
            {
                b.OpenElement(0, "span");
                b.AddAttribute(1, "class", "af-null");
                b.AddContent(2, "—");
                b.CloseElement();
                return;
            }
            if (column.Content is { } kind && value is string content)
            {
                b.OpenComponent<ContentCell>(3);
                b.AddComponentParameter(4, nameof(ContentCell.Value), content);
                b.AddComponentParameter(5, nameof(ContentCell.Kind), kind);
                b.AddComponentParameter(6, nameof(ContentCell.Title), column.Title);
                b.CloseComponent();
                return;
            }
            if (column.Kind == ColumnKind.Collection && value is IEnumerable items)
            {
                b.OpenComponent<CollectionView>(7);
                b.AddComponentParameter(8, nameof(CollectionView.Items), items);
                b.AddComponentParameter(9, nameof(CollectionView.ElementType), column.ElementType);
                b.CloseComponent();
                return;
            }
            if (column.LinksTo is { } target)
            {
                b.OpenElement(10, "a");
                b.AddAttribute(11, "href", Links.Row(target, Key(value)));
                b.AddContent(12, link ?? Text(value, column, panel));
                b.CloseElement();
                return;
            }
            if (Tone(value, column) is { } tone)
            {
                b.OpenElement(13, "span");
                b.AddAttribute(14, "class", $"af-chip {ToneClass(tone)}");
                b.AddContent(15, Text(value, column, panel));
                b.CloseElement();
                return;
            }
            if (column.IsKey || value is Guid)
            {
                b.OpenElement(16, "span");
                b.AddAttribute(17, "class", "af-mono");
                b.AddContent(18, Text(value, column, panel));
                b.CloseElement();
                return;
            }
            b.AddContent(19, Text(value, column, panel));
        };

    public static string Text(object value, ColumnModel column, PanelModel panel) =>
        ValueFormatter.Format(value, column, panel) ?? "";

    /// <summary>The column's tone for <paramref name="value"/>: its own map, else success for true and neutral for false or an enum.</summary>
    public static Tone? Tone(object? value, ColumnModel column) =>
        value is null ? null
        : column.Tones is { } tones ? tones.GetValueOrDefault(value, AdminForge.Tone.Neutral)
        : value is true ? AdminForge.Tone.Success
        : value is bool or Enum ? AdminForge.Tone.Neutral
        : null;

    public static string ToneClass(Tone tone) => $"af-tone-{tone.ToString().ToLowerInvariant()}";

    /// <summary>Right-aligned: a number that is not a key or a link.</summary>
    public static string? Align(ColumnModel column) =>
        !column.IsKey
        && column.LinksTo is null
        && column.Tones is null
        && (Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType) is var t
        && (ScalarTypes.IsInteger(t) || ScalarTypes.IsFraction(t))
            ? "af-num"
            : null;

    /// <summary>A fixed width for a column whose values have a known length; text shares the rest.</summary>
    public static string? Width(ColumnModel column)
    {
        var t = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;
        return column.LinksTo is not null
            || column.Content is not null
            || column.Kind != ColumnKind.Value
                ? null
            : t == typeof(DateTime) || t == typeof(DateTimeOffset) ? "width: 21ch"
            : t == typeof(DateOnly) ? "width: 15ch"
            : t == typeof(bool) || t.IsEnum || column.Tones is not null ? "width: 17ch"
            : Align(column) is not null || column.IsKey && t != typeof(string) ? "width: 12ch"
            : t == typeof(Guid) ? "width: 40ch"
            : null;
    }

    private static bool Plain(object? value, ColumnModel column) =>
        column.Content is null
        && column.Kind == ColumnKind.Value
        && column.LinksTo is null
        && Tone(value, column) is null;

    private static string Key(object value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
