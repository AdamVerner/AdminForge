using System.Reflection;
using System.Security.Claims;

namespace AdminForge;

/// <summary>Where the shell lists an entry.</summary>
public enum NavArea
{
    /// <summary>The sidebar's main list.</summary>
    Main,

    /// <summary>The panel's own configuration, pinned to the bottom of the sidebar.</summary>
    Settings,

    /// <summary>The signed-in admin's own things, in the account menu.</summary>
    Account,
}

/// <summary>How a long text value opens; the cell shows a one-line preview.</summary>
public enum Content
{
    Text,
    Markdown,

    /// <summary>Re-indented; a value that is not JSON shows as text.</summary>
    Json,

    /// <summary>In a sandboxed frame: no scripts, no access to the panel.</summary>
    Html,
}

/// <summary>The colour of a status chip or an action button.</summary>
public enum Tone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>The deployment the panel runs in; each has its own colour so they are not mistaken for each other.</summary>
public enum AdminEnvironment
{
    Local,
    Development,
    Staging,
    Production,
}

/// <summary>The shell's logo, accent and extra stylesheet. Anything left null keeps the default.</summary>
public sealed class ThemeOptions
{
    /// <summary>An absolute URL, an app-relative path or a <c>data:</c> URI.</summary>
    public string? LogoUrl { get; set; }
    public string? LogoAlt { get; set; }

    /// <summary>The accent of buttons, links and the active nav entry, as a hex colour such as <c>#2457C5</c>.</summary>
    public string? PrimaryColor { get; set; }

    /// <summary>Loaded after the panel's own stylesheet, so it can override any <c>--af-*</c> token.</summary>
    public string? StylesheetUrl { get; set; }
}

/// <summary>Where a host component renders inside the panel's own pages.</summary>
public enum AdminSlot
{
    AboveHome,
    BelowHome,

    /// <summary>Between a detail page's header and its sections; the component receives <c>TableId</c> and <c>Key</c>.</summary>
    AboveDetail,

    /// <summary>After a detail page's related tables; the component receives <c>TableId</c> and <c>Key</c>.</summary>
    BelowDetail,
}

internal sealed record PanelModel
{
    public const string Root = "/admin";

    public required string Title { get; init; }
    public string? Welcome { get; init; }
    public (string Label, AdminEnvironment Kind)? Environment { get; init; }
    public required ThemeOptions Theme { get; init; }
    public string? SignOutPath { get; init; }
    public string? Policy { get; init; }
    public bool AllowAnonymous { get; init; }
    public required IReadOnlyList<TableModel> Tables { get; init; }
    public required IReadOnlyList<FormModel> Forms { get; init; }
    public required IReadOnlyList<DashboardModel> Dashboards { get; init; }
    public required IReadOnlyList<PageModel> Pages { get; init; }

    /// <summary>The assemblies the host's own pages live in, so the router finds their routes.</summary>
    public IEnumerable<Assembly> HostAssemblies =>
        Pages.Select(p => p.Component.Assembly).Where(a => a != GetType().Assembly).Distinct();
    public required IReadOnlyDictionary<Type, string> Formats { get; init; }
    public IReadOnlyList<(AdminSlot Slot, Type Component)> Slots { get; init; } = [];

    public DashboardModel? Home => Dashboards.FirstOrDefault(d => d.Home);

    public TableModel? Table(string id) =>
        Tables.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public FormModel? Form(string id) =>
        Forms.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    public DashboardModel? Dashboard(string id) =>
        Dashboards.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary><see cref="Order"/> is the registration index; the sidebar keeps that order.</summary>
internal sealed record NavModel(string? Group, NavArea Area, bool Hidden, int Order);

internal sealed record TableModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required Type ClrType { get; init; }
    public required NavModel Nav { get; init; }

    /// <summary>Every column, in the order the detail page and the edit form show them.</summary>
    public required IReadOnlyList<ColumnModel> Columns { get; init; }
    public required KeyCodec Key { get; init; }
    public required SourceModel Source { get; init; }
    public required RowFactory Rows { get; init; }
    public required Func<object, string> Label { get; init; }
    public required IReadOnlyList<ActionModel> Actions { get; init; }
    public required IReadOnlyList<RelatedModel> Related { get; init; }
    public required IReadOnlyList<LiveModel> Live { get; init; }
    public TimeSpan? Poll { get; init; }
    public string? Empty { get; init; }

    /// <summary>Filters that match by equality even on text: a related table's pin and link columns.</summary>
    public IReadOnlySet<string> Exact { get; init; } = new HashSet<string>();
    public Func<ClaimsPrincipal, AdminOp, object?, bool>? Authorize { get; init; }
    public required Func<ClaimsPrincipal, AdminOp, object?, AdminRequest> Request { get; init; }
    public required Func<
        ClaimsPrincipal,
        string,
        object?,
        AdminRequest
    > ActionRequest { get; init; }

    public IEnumerable<ColumnModel> ListColumns =>
        Columns.Where(c => c.InList).OrderBy(c => c.ListOrder);

    public bool HasDetail => Source.Find is not null;
    public bool Searchable => Source.Search;

    public ColumnModel? Column(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public ActionModel? Action(string id) =>
        Actions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The columns a create or an edit form offers.</summary>
    public IEnumerable<ColumnModel> Editable(bool creating) =>
        Columns.Where(c => c.Edit is not null && (creating ? !c.IsKey || c.KeyEditable : !c.IsKey));
}

internal enum ColumnKind
{
    Value,

    /// <summary>A list carried by the row: detail page only, never edited.</summary>
    Collection,
}

internal sealed record ColumnModel
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required Type ClrType { get; init; }
    public ColumnKind Kind { get; init; }
    public Type? ElementType { get; init; }
    public bool IsKey { get; init; }

    /// <summary>A key the admin types on create; a generated key never is.</summary>
    public bool KeyEditable { get; init; }

    /// <summary>Null for a computed column.</summary>
    public PropertyInfo? Property { get; init; }
    public Func<IServiceProvider, object, CancellationToken, Task<object?>>? Compute { get; init; }
    public bool InList { get; init; }
    public int ListOrder { get; init; }
    public string? Format { get; init; }
    public Content? Content { get; init; }

    /// <summary>The id of the table this column's value is a key of.</summary>
    public string? LinksTo { get; init; }
    public IReadOnlyDictionary<object, Tone>? Tones { get; init; }

    /// <summary>The input that edits this column; null when nothing writes it.</summary>
    public FieldModel? Edit { get; init; }
    public bool Filterable { get; init; }
    public bool Sortable { get; init; }

    public object? Read(object row) => Property?.GetValue(row);
}

internal enum FieldKind
{
    Text,
    Multiline,
    Markdown,
    Integer,
    Number,
    Bool,
    Date,
    DateTime,
    Enum,
    Select,
    File,
}

internal sealed record FieldModel
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required Type ClrType { get; init; }
    public required FieldKind Kind { get; init; }
    public bool Required { get; init; }
    public int? MaxLength { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public IReadOnlyList<Func<object?, string?>> Rules { get; init; } = [];
    public Func<
        IServiceProvider,
        CancellationToken,
        Task<IReadOnlyList<AdminOption>>
    >? Options { get; init; }

    /// <summary>Choices loaded with the row an action runs on; without a row there are none to list.</summary>
    public Func<
        IServiceProvider,
        object,
        CancellationToken,
        Task<IReadOnlyList<AdminOption>>
    >? RowOptions { get; init; }
    public bool Multiple { get; init; }
    public IReadOnlyList<string>? Accept { get; init; }
    public long? MaxBytes { get; init; }
}

/// <summary>An input record's fields and the way to build one from validated values.</summary>
internal sealed record InputModel(IReadOnlyList<FieldModel> Fields, RowFactory Factory);

internal sealed record ActionModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Confirm { get; init; }
    public Tone Tone { get; init; }
    public InputModel? Input { get; init; }

    /// <summary>False hides the action on that row and refuses it there; authorization is asked separately.</summary>
    public Func<object, bool>? When { get; init; }

    /// <summary>The input the dialog opens with for a row, in place of the input type's defaults.</summary>
    public Func<IServiceProvider, object, CancellationToken, Task<object>>? Prefill { get; init; }

    /// <summary>Services of the operation, the row, the input (null without one), the user, the cancellation.</summary>
    public required Func<
        IServiceProvider,
        object,
        object?,
        ClaimsPrincipal,
        CancellationToken,
        Task<AdminContext>
    > Run { get; init; }
}

internal sealed record FormModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required NavModel Nav { get; init; }
    public required InputModel Input { get; init; }
    public required Func<
        IServiceProvider,
        object,
        ClaimsPrincipal,
        CancellationToken,
        Task<AdminContext>
    > Run { get; init; }
}

/// <summary>A child table pinned to the parent's key through the child's <see cref="ForeignKey"/> column.</summary>
internal sealed record RelatedModel(
    string Title,
    string TableId,
    string ForeignKey,
    bool Inline,
    IReadOnlyList<string>? Columns
);

/// <summary>A stream of the row shown on its detail page, as a chart (cells are x and y) or a newest-first feed.</summary>
internal sealed record LiveModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required Func<
        IServiceProvider,
        object,
        CancellationToken,
        IAsyncEnumerable<object>
    > Stream { get; init; }
    public required IReadOnlyList<(string Title, Func<object, object?> Read)> Cells { get; init; }
    public bool Chart { get; init; }
    public int Keep { get; init; }
}

internal sealed record DashboardModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required NavModel Nav { get; init; }
    public bool Home { get; init; }
    public required IReadOnlyList<IReadOnlyList<WidgetModel>> Rows { get; init; }

    public WidgetModel? Widget(string id) =>
        Rows.SelectMany(r => r).FirstOrDefault(w => w.Id == id);
}

internal abstract record WidgetModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
}

internal sealed record StatModel : WidgetModel
{
    public required Func<IServiceProvider, CancellationToken, Task<object?>> Fetch { get; init; }
    public string? Suffix { get; init; }
}

internal sealed record ChartModel : WidgetModel
{
    public Func<
        IServiceProvider,
        CancellationToken,
        Task<IReadOnlyList<object>>
    >? Points { get; init; }
    public Func<
        IServiceProvider,
        CancellationToken,
        IAsyncEnumerable<object>
    >? Stream { get; init; }
    public required Func<object, object?> X { get; init; }
    public required Func<object, object?> Y { get; init; }
    public string? YLabel { get; init; }
    public TimeSpan? Poll { get; init; }
    public int Window { get; init; } = 50;
}

internal sealed record TableWidgetModel : WidgetModel
{
    public required string TableId { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
    public int Take { get; init; } = 10;
}

internal sealed record PageModel(
    string Id,
    Type Component,
    string Title,
    string Route,
    NavModel Nav
);
