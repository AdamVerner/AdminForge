using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Composes a row-based dashboard layout. Each <see cref="Row"/> call appends a
/// <see cref="LayoutRowMeta"/>; within a row, cells reference widgets by title
/// (matched case-insensitively against the widget's <c>Title</c>) and carry a
/// relative width.
/// </summary>
public sealed class LayoutBuilder
{
    private readonly IReadOnlyList<WidgetMeta> _widgets;
    private readonly List<LayoutRowMeta> _rows = [];

    internal LayoutBuilder(IReadOnlyList<WidgetMeta> widgets)
    {
        _widgets = widgets;
    }

    /// <summary>Appends a row, configured via the supplied callback.</summary>
    public LayoutBuilder Row(Action<RowBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var rb = new RowBuilder(_widgets);
        configure(rb);
        _rows.Add(rb.Build());
        return this;
    }

    internal DashboardLayoutMeta Build() => new() { Rows = _rows.AsReadOnly() };
}

/// <summary>
/// Composes the cells of a single row inside a <see cref="LayoutBuilder"/>.
/// </summary>
public sealed class RowBuilder
{
    private readonly IReadOnlyList<WidgetMeta> _widgets;
    private readonly List<LayoutCellMeta> _cells = [];

    internal RowBuilder(IReadOnlyList<WidgetMeta> widgets)
    {
        _widgets = widgets;
    }

    /// <summary>
    /// Adds a cell referencing the widget whose <c>Title</c> matches <paramref name="widgetTitle"/>
    /// (case-insensitive). Throws if no matching widget exists on the parent dashboard.
    /// </summary>
    public RowBuilder Add(string widgetTitle, int width = 1, bool fullWidth = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetTitle);
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");

        var widget =
            _widgets.FirstOrDefault(w =>
                string.Equals(w.Title, widgetTitle, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"No widget with title '{widgetTitle}' has been registered on this dashboard."
            );

        _cells.Add(
            new LayoutCellMeta
            {
                WidgetId = widget.Id,
                Width = width,
                FullWidth = fullWidth,
            }
        );
        return this;
    }

    internal LayoutRowMeta Build() => new() { Cells = _cells.AsReadOnly() };
}
