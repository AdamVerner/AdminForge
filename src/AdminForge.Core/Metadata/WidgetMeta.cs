namespace AdminForge.Core.Metadata;

/// <summary>
/// Identifies the concrete widget shape carried by a <see cref="WidgetMeta"/>.
/// </summary>
public enum WidgetKind
{
    StatCard,
    LineChart,
    Table,
}

/// <summary>
/// Abstract base for dashboard widget metadata. Concrete subclasses are POCOs
/// produced by the fluent builders in <c>AdminForge.Core.Configuration</c>.
/// </summary>
public abstract class WidgetMeta
{
    /// <summary>
    /// Stable identifier used by the layout system to place this widget into a
    /// specific cell, and as the React key during diffing. Unique within a dashboard.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Display title shown above the widget.</summary>
    public required string Title { get; init; }

    /// <summary>The concrete widget kind — drives renderer dispatch.</summary>
    public abstract WidgetKind Kind { get; }
}

/// <summary>
/// A single-value status indicator (e.g. "Open todos: 42"). The provider delegate
/// is invoked once per page load (Phase 3) within a scoped <see cref="IServiceProvider"/>.
/// </summary>
public sealed class StatCardMeta : WidgetMeta
{
    public override WidgetKind Kind => WidgetKind.StatCard;

    /// <summary>
    /// Provider delegate returning the value to display. The framework supplies a
    /// scoped <see cref="IServiceProvider"/> so the user can resolve their <c>DbContext</c>
    /// or other scoped services.
    /// </summary>
    public required Func<IServiceProvider, CancellationToken, Task<object?>> Fetch { get; init; }

    /// <summary>Optional suffix (e.g. "todos", "%").</summary>
    public string? Suffix { get; init; }
}

/// <summary>
/// A 2-D series widget rendered as a line chart. The selectors are stored as
/// non-generic delegates over <see cref="object"/> so the meta stays POCO; the
/// builders close over the user's typed expressions when populating them.
/// </summary>
public sealed class LineChartMeta : WidgetMeta
{
    public override WidgetKind Kind => WidgetKind.LineChart;

    /// <summary>
    /// Provider delegate returning the raw data points to project. Items are boxed —
    /// concrete element types are captured behind <see cref="XSelector"/>/<see cref="YSelector"/>.
    /// </summary>
    public required Func<
        IServiceProvider,
        CancellationToken,
        Task<IReadOnlyList<object>>
    > Fetch { get; init; }

    /// <summary>Projects an item to its X-axis value (typically a date/time or numeric).</summary>
    public required Func<object, object?> XSelector { get; init; }

    /// <summary>Projects an item to its Y-axis value (must be numeric or numeric-convertible).</summary>
    public required Func<object, object?> YSelector { get; init; }

    /// <summary>Optional axis labels for the chart.</summary>
    public string? XAxisLabel { get; init; }

    /// <summary>Optional axis labels for the chart.</summary>
    public string? YAxisLabel { get; init; }
}

/// <summary>
/// A read-only entity table embedded in a dashboard. Reuses the existing entity
/// list infrastructure via <see cref="EntityType"/> + an optional row limit.
/// </summary>
public sealed class TableWidgetMeta : WidgetMeta
{
    public override WidgetKind Kind => WidgetKind.Table;

    /// <summary>The CLR type of the entity to list. Must be a registered entity.</summary>
    public required Type EntityType { get; init; }

    /// <summary>Maximum rows to fetch; null = the bridge's default page size.</summary>
    public int? MaxRows { get; init; }

    /// <summary>
    /// Optional projection: subset of property names to display. Null = inherit
    /// the entity's default visible columns.
    /// </summary>
    public IReadOnlyList<string>? VisibleColumns { get; init; }

    /// <summary>Sort property name (defaults to PK).</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction.</summary>
    public bool SortDescending { get; init; }
}
