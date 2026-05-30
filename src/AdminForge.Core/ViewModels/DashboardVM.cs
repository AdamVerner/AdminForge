using AdminForge.Core.Metadata;

namespace AdminForge.Core.ViewModels;

/// <summary>
/// Materialised dashboard view: title + layout + per-widget VMs in the same
/// order as <see cref="DashboardMeta.Widgets"/>.
/// </summary>
public sealed class DashboardVM
{
    /// <summary>Stable route name (matches <see cref="DashboardMeta.RouteName"/>).</summary>
    public required string RouteName { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Materialised widget VMs keyed by widget id.</summary>
    public required IReadOnlyDictionary<string, WidgetVM> Widgets { get; init; }

    /// <summary>Row-based layout, when configured.</summary>
    public DashboardLayoutMeta? Layout { get; init; }
}

/// <summary>Base type for materialised widget view models.</summary>
public abstract class WidgetVM
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public abstract WidgetKind Kind { get; }

    /// <summary>True when the widget's fetch delegate threw.</summary>
    public string? Error { get; init; }
}

/// <summary>Materialised view of a <see cref="StatCardMeta"/>.</summary>
public sealed class StatCardVM : WidgetVM
{
    public override WidgetKind Kind => WidgetKind.StatCard;
    public object? Value { get; init; }
    public string? Suffix { get; init; }
}

/// <summary>One point on a line chart.</summary>
public sealed record LineChartPoint(object? X, double Y);

/// <summary>Materialised view of a <see cref="LineChartMeta"/>.</summary>
public sealed class LineChartVM : WidgetVM
{
    public override WidgetKind Kind => WidgetKind.LineChart;
    public required IReadOnlyList<LineChartPoint> Points { get; init; }
    public string? XAxisLabel { get; init; }
    public string? YAxisLabel { get; init; }
}

/// <summary>Materialised view of a <see cref="TableWidgetMeta"/>.</summary>
public sealed class TableWidgetVM : WidgetVM
{
    public override WidgetKind Kind => WidgetKind.Table;
    public required EntityMeta EntityMeta { get; init; }
    public required IReadOnlyList<string> VisibleColumns { get; init; }
    public required IReadOnlyList<EntityListRowVM> Rows { get; init; }
}
