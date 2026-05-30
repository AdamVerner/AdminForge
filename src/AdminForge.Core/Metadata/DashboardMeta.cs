namespace AdminForge.Core.Metadata;

/// <summary>
/// A dashboard page — a titled collection of <see cref="WidgetMeta"/> instances arranged
/// into a row-based grid <see cref="Layout"/>. Built fluently via <c>DashboardBuilder</c>.
/// </summary>
public sealed class DashboardMeta
{
    /// <summary>
    /// Stable URL-safe route name for this dashboard (e.g. "operations").
    /// Used to resolve <c>/admin/dashboards/{routeName}</c>.
    /// </summary>
    public required string RouteName { get; init; }

    /// <summary>Display title shown above the dashboard grid.</summary>
    public required string Title { get; set; }

    /// <summary>Sidebar nav entry.</summary>
    public NavMeta Nav { get; set; } = new();

    /// <summary>
    /// Widgets in registration order. Layout cells reference widgets by
    /// <see cref="WidgetMeta.Id"/>.
    /// </summary>
    public IReadOnlyList<WidgetMeta> Widgets { get; init; } = [];

    /// <summary>
    /// Row-based layout. If null, the renderer falls back to one widget per full-width row
    /// in registration order.
    /// </summary>
    public DashboardLayoutMeta? Layout { get; init; }
}

/// <summary>
/// Row-based grid layout produced by <c>LayoutBuilder</c>. Each row holds cells; each
/// cell references a widget by id and carries a width (default 1, fullWidth = spans the row).
/// </summary>
public sealed class DashboardLayoutMeta
{
    public required IReadOnlyList<LayoutRowMeta> Rows { get; init; }
}

/// <summary>One row in a <see cref="DashboardLayoutMeta"/>.</summary>
public sealed class LayoutRowMeta
{
    public required IReadOnlyList<LayoutCellMeta> Cells { get; init; }
}

/// <summary>
/// One cell within a <see cref="LayoutRowMeta"/>. Either spans the full row
/// (<see cref="FullWidth"/>) or occupies <see cref="Width"/> column units.
/// Total width per row is summed at render time and used to compute MudGrid xs.
/// </summary>
public sealed class LayoutCellMeta
{
    /// <summary>References <see cref="WidgetMeta.Id"/> (case-sensitive).</summary>
    public required string WidgetId { get; init; }

    /// <summary>Relative width within the row (default 1). Ignored when <see cref="FullWidth"/> is true.</summary>
    public int Width { get; init; } = 1;

    /// <summary>If true, this cell spans the entire row regardless of <see cref="Width"/>.</summary>
    public bool FullWidth { get; init; }
}
