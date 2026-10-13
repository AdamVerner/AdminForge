namespace AdminForge;

internal sealed record ListRequest(
    int Page = 0,
    int PageSize = 25,
    string? Search = null,
    string? SortBy = null,
    bool Descending = false,
    IReadOnlyDictionary<string, object?>? Filters = null,
    IReadOnlySet<string>? Exact = null,
    IReadOnlyCollection<string>? Columns = null
);

/// <summary>What the caller may do to one row, decided by the pipeline with the row.</summary>
internal sealed record Allowed(bool Update, bool Delete, IReadOnlySet<string> Actions);

/// <summary>A row as a surface shows it: its key, its label and its column values, never the row itself.</summary>
internal sealed record RowView(
    string Key,
    string Label,
    IReadOnlyDictionary<string, object?> Values,
    Allowed Allowed
)
{
    /// <summary>Link column → the linked row's label, for the rows the caller may read.</summary>
    public IReadOnlyDictionary<string, string> Links { get; init; } =
        new Dictionary<string, string>();
}

internal sealed record ListView(
    string TableId,
    IReadOnlyList<RowView> Rows,
    int Total,
    int Page,
    int PageSize,
    bool CanCreate
);

/// <summary>A related table and the filter that pins it to this row.</summary>
internal sealed record RelatedView(RelatedModel Model, IReadOnlyDictionary<string, object?> Filter);

internal sealed record DetailView(RowView Row, IReadOnlyList<RelatedView> Related);

/// <summary>The inputs of a create, an edit, an action or a form, their current values and their choices.</summary>
internal sealed record FormView(
    IReadOnlyList<FieldModel> Fields,
    Dictionary<string, object?> Values,
    IReadOnlyDictionary<string, IReadOnlyList<AdminOption>> Options
)
{
    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);
}

/// <summary>What a handler reported: a one-line message and a Markdown result.</summary>
/// <param name="Open">The key of the row to open next, in the action's table.</param>
internal sealed record Outcome(string? Message, string? Markdown, string? Open = null);

internal sealed record DashboardView(
    DashboardModel Model,
    IReadOnlyDictionary<string, WidgetView> Widgets
);

internal abstract record WidgetView(string Id, string Title)
{
    public string? Error { get; init; }
}

internal sealed record StatView(string Id, string Title, object? Value, string? Suffix)
    : WidgetView(Id, Title);

internal sealed record ChartView(
    string Id,
    string Title,
    IReadOnlyList<(object? X, double Y)> Points,
    string? YLabel
) : WidgetView(Id, Title);

internal sealed record TableWidgetView(
    string Id,
    string Title,
    string TableId,
    IReadOnlyList<string> Columns,
    IReadOnlyList<RowView> Rows
) : WidgetView(Id, Title);

internal sealed record SearchHit(string TableId, string TableTitle, string Key, string Label);

internal sealed record NavItem(string Id, string Title, string Href, NavModel Nav);
