using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>One table: its columns, source, related tables, actions and live widgets.</summary>
/// <remarks>
/// Every scalar property is a column, in declaration order until <see cref="Columns"/> selects; a
/// list property shows on the detail page only. What the source implements decides whether the
/// table offers a detail page, create, edit and delete.
/// </remarks>
public sealed class TableBuilder<T>
    where T : class
{
    private readonly Dictionary<string, ColumnSettings> _columns = new(StringComparer.Ordinal);
    private readonly List<ComputedColumn> _computed = [];
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private readonly List<ActionModel> _actions = [];
    private readonly List<Func<BuildContext, RelatedModel>> _related = [];
    private readonly List<LiveModel> _live = [];
    private NavModel _nav;
    private string _title = Members.Humanize(typeof(T).Name);
    private List<string>? _listed;
    private Func<BuildContext, SourceModel>? _source;
    private Func<T, string>? _label;
    private TimeSpan? _poll;
    private Func<T, bool>? _deleteWhen;
    private string? _empty;
    private Func<ClaimsPrincipal, AdminOp, T?, bool>? _authorize;

    internal TableBuilder(NavModel nav) => _nav = nav;

    internal string TableId { get; private set; } = Members.Kebab(typeof(T).Name);

    public TableBuilder<T> Id(string id)
    {
        TableId = id;
        return this;
    }

    public TableBuilder<T> Title(string title)
    {
        _title = title;
        return this;
    }

    public TableBuilder<T> Area(NavArea area)
    {
        _nav = _nav with { Area = area };
        return this;
    }

    /// <summary>Leaves the table out of the sidebar; it stays routable and reachable from links.</summary>
    public TableBuilder<T> Hidden()
    {
        _nav = _nav with { Hidden = true };
        return this;
    }

    /// <summary>The list page's columns, in this order. The detail page and the forms follow the same order.</summary>
    public TableBuilder<T> Columns(params Expression<Func<T, object?>>[] columns)
    {
        _listed = columns.Select(Members.Name).ToList();
        return this;
    }

    /// <summary>Configures a column; it does not add or list it.</summary>
    public TableBuilder<T> Column<TProp>(
        Expression<Func<T, TProp>> column,
        Action<ColumnBuilder<TProp>> configure
    )
    {
        var name = Members.Name(column);
        configure(new ColumnBuilder<TProp>(Settings(name)));
        return this;
    }

    /// <summary>Removes columns from every surface; writes leave them as stored.</summary>
    public TableBuilder<T> Hide(params Expression<Func<T, object?>>[] columns)
    {
        foreach (var c in columns)
            _hidden.Add(Members.Name(c));
        return this;
    }

    /// <summary>A value computed from the row, on the detail page unless the column calls <c>InList()</c>.</summary>
    public TableBuilder<T> Computed<TValue>(
        string name,
        Func<T, TValue> value,
        Action<ColumnBuilder<TValue>>? configure = null
    ) =>
        AddComputed(
            name,
            typeof(TValue),
            (_, row, _) => Task.FromResult<object?>(value((T)row)),
            configure is null ? null : s => configure(new ColumnBuilder<TValue>(s))
        );

    /// <summary>A value computed by <typeparamref name="TService"/>; one call per row shown.</summary>
    public TableBuilder<T> Computed<TService, TValue>(
        string name,
        Func<TService, T, CancellationToken, Task<TValue>> value,
        Action<ColumnBuilder<TValue>>? configure = null
    )
        where TService : notnull =>
        AddComputed(
            name,
            typeof(TValue),
            async (sp, row, ct) =>
                await value(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), (T)row, ct)
                    .ConfigureAwait(false),
            configure is null ? null : s => configure(new ColumnBuilder<TValue>(s))
        );

    /// <summary>The row's label wherever it is named. Defaults to its Name, Title, Label, DisplayName or Email, else its key.</summary>
    public TableBuilder<T> DisplayMember(Func<T, object?> label)
    {
        _label = row => label(row)?.ToString() ?? "";
        return this;
    }

    /// <summary>Lists through <typeparamref name="TService"/>. Without a find the table has no detail page.</summary>
    public TableBuilder<T> Source<TService>(
        Func<TService, AdminQuery<T>, CancellationToken, Task<AdminPage<T>>> list,
        Action<SourceOptions<T>>? configure = null
    )
        where TService : class
    {
        var options = Options(configure);
        _source = _ => Sources.Inline(list, options);
        return this;
    }

    /// <summary>Lists and finds through <typeparamref name="TService"/>; <typeparamref name="TKey"/> is the key's type.</summary>
    public TableBuilder<T> Source<TService, TKey>(
        Func<TService, AdminQuery<T>, CancellationToken, Task<AdminPage<T>>> list,
        Func<TService, TKey, CancellationToken, Task<T?>> find,
        Action<SourceOptions<T>>? configure = null
    )
        where TService : class
    {
        var options = Options(configure);
        _source = _ => Sources.Inline(list, find, options);
        return this;
    }

    /// <summary>A source class implementing <see cref="IAdminSource{T, TKey}"/>, resolved per operation.</summary>
    /// <remarks>It gets create and edit with <see cref="IAdminWritable{T}"/>, delete with <see cref="IAdminDeletable{T}"/>.</remarks>
    public TableBuilder<T> Source<TSource>(Action<SourceOptions<T>>? configure = null)
        where TSource : class
    {
        var options = configure is null ? null : Options(configure);
        _source = _ => Sources.Class<T, TSource>(options);
        return this;
    }

    /// <summary>The rows of <typeparamref name="TChild"/> whose <paramref name="foreignKey"/> holds this row's key, on the detail page.</summary>
    public TableBuilder<T> Related<TChild>(
        Expression<Func<TChild, object?>> foreignKey,
        Action<RelatedBuilder<TChild>>? configure = null
    )
        where TChild : class
    {
        var related = new RelatedBuilder<TChild>(Members.Name(foreignKey));
        configure?.Invoke(related);
        var owner = TableId;
        _related.Add(ctx => related.Build(ctx, $"Table '{owner}' Related<{typeof(TChild).Name}>"));
        return this;
    }

    /// <summary>A button on the row. Its id is its policy key, its audit name and part of its MCP tool name.</summary>
    public TableBuilder<T> Action(string id, string title, Action<ActionBuilder<T>> configure)
    {
        var action = new ActionBuilder<T>(id, title);
        configure(action);
        if (_actions.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Table '{TableId}' has two actions with id '{id}'."
            );
        _actions.Add(action.Build(TableId));
        return this;
    }

    /// <summary>A stream of this row from <typeparamref name="TService"/>, shown on its detail page as a chart or a feed.</summary>
    public TableBuilder<T> Live<TService, TPoint>(
        string title,
        Func<TService, T, CancellationToken, IAsyncEnumerable<TPoint>> stream,
        Action<LiveBuilder<TPoint>> configure
    )
        where TService : notnull
        where TPoint : notnull
    {
        var live = new LiveBuilder<TPoint>();
        configure(live);
        _live.Add(
            live.Build(
                title,
                (sp, row, ct) =>
                    stream(ActivatorUtilities.GetServiceOrCreateInstance<TService>(sp), (T)row, ct)
                        .Select(p => (object)p)
            )
        );
        return this;
    }

    /// <summary>One sentence under the empty list, such as how rows get there.</summary>
    public TableBuilder<T> Empty(string text)
    {
        _empty = text;
        return this;
    }

    /// <summary>The detail page re-reads the row every <paramref name="interval"/>.</summary>
    public TableBuilder<T> Poll(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _poll = interval;
        return this;
    }

    /// <summary>Offers Delete only on the rows <paramref name="when"/> holds for; deleting any other is refused.</summary>
    public TableBuilder<T> DeleteWhen(Func<T, bool> when)
    {
        _deleteWhen = when;
        return this;
    }

    /// <summary>A rule asked on every operation on this table, with the stored row whenever there is one.</summary>
    /// <remarks>An action asks it as an <see cref="AdminOp.Update"/> of its row.</remarks>
    public TableBuilder<T> Authorize(Func<ClaimsPrincipal, AdminOp, T?, bool> rule)
    {
        _authorize = rule;
        return this;
    }

    private ColumnSettings Settings(string name) =>
        _columns.TryGetValue(name, out var s) ? s : _columns[name] = new ColumnSettings();

    private TableBuilder<T> AddComputed(
        string name,
        Type type,
        Func<IServiceProvider, object, CancellationToken, Task<object?>> compute,
        Action<ColumnSettings>? configure
    )
    {
        var settings = new ColumnSettings();
        configure?.Invoke(settings);
        _computed.Add(new ComputedColumn(name, type, compute, settings));
        return this;
    }

    private static SourceOptions<T> Options(Action<SourceOptions<T>>? configure)
    {
        var options = new SourceOptions<T>();
        configure?.Invoke(options);
        return options;
    }

    internal TableModel Build(BuildContext ctx)
    {
        var who = $"Table '{TableId}'";
        var shape = ctx.Shape(typeof(T));
        var rows = new RowFactory(typeof(T));
        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead
                && p.GetIndexParameters().Length == 0
                && shape?.NotColumns.Contains(p.Name) != true
            )
            .ToList();
        foreach (var name in _columns.Keys.Concat(_listed ?? []).Concat(_hidden))
            if (properties.All(p => p.Name != name))
                throw new InvalidOperationException($"{who} has no column '{name}'.");

        var keyNames =
            shape?.Keys
            ?? properties
                .Where(p => p.IsDefined(typeof(KeyAttribute)))
                .Select(p => p.Name)
                .ToList();
        if (keyNames.Count == 0 && properties.Any(p => p.Name == "Id"))
            keyNames = ["Id"];
        if (keyNames.Count == 0)
            throw new InvalidOperationException(
                $"{who}: {typeof(T).Name} has no key. Mark one or more properties [Key], or name one Id."
            );
        var key = new KeyCodec(keyNames.Select(k => properties.First(p => p.Name == k)).ToList());

        var source =
            _source?.Invoke(ctx)
            ?? (shape is null ? null : ctx.DefaultSource(typeof(T)))
            ?? throw new InvalidOperationException(
                $"{who} has no source. Give it one with t.Source(...), or serve {typeof(T).Name} from EF with panel.UseEntityFramework<TDb>()."
            );
        if (
            source.Find is null
            && (
                source.Update is not null
                || source.Delete is not null
                || _actions.Count > 0
                || _live.Count > 0
                || _poll is not null
            )
        )
            throw new InvalidOperationException(
                $"{who}: its source cannot find a row by key, which edit, delete, actions, live widgets and polling all start from."
            );
        var keyType =
            key.Properties.Count == 1 ? key.Properties[0].PropertyType : typeof(object?[]);
        if (
            source.KeyType is { } declared
            && declared != typeof(object?[])
            && (Nullable.GetUnderlyingType(keyType) ?? keyType) != declared
        )
            throw new InvalidOperationException(
                $"{who}: its source finds by {declared.Name}, but the key {string.Join(",", keyNames)} is a {keyType.Name}."
            );

        ColumnModel Column(string name, Type type, ColumnSettings s) =>
            new()
            {
                Name = name,
                Title = s.Field.Title ?? Members.Humanize(name),
                Description = s.Field.Description,
                ClrType = type,
                Format = s.Format,
                Content = s.Content,
                LinksTo =
                    s.LinksToType is null && s.LinksToId is null
                        ? null
                        : ctx.TableFor(s.LinksToType, s.LinksToId, $"{who} column {name}"),
                Tones = s.Tones,
            };

        var listed = _listed ?? properties.Select(p => p.Name).ToList();
        var ordered = properties
            .Where(p => !_hidden.Contains(p.Name))
            .OrderBy(p => listed.IndexOf(p.Name) is var i and >= 0 ? i : int.MaxValue)
            .ToList();
        var columns = new List<ColumnModel>();
        foreach (var p in ordered)
        {
            var isScalar = ScalarTypes.IsScalar(p.PropertyType);
            var element = isScalar ? null : ScalarTypes.ElementType(p.PropertyType);
            if (!isScalar && element is null)
                continue;
            var s = _columns.GetValueOrDefault(p.Name) ?? new ColumnSettings();
            var isKey = keyNames.Contains(p.Name);
            var generated = shape?.Generated.Contains(p.Name) == true;
            var stored = shape?.Unmapped.Contains(p.Name) != true;
            var writable = isScalar && stored && rows.CanSet(p) && !s.ReadOnly && !generated;
            var keyEditable = isKey && shape is not null && writable;
            columns.Add(
                Column(p.Name, p.PropertyType, s) with
                {
                    Kind = isScalar ? ColumnKind.Value : ColumnKind.Collection,
                    ElementType = element,
                    IsKey = isKey,
                    KeyEditable = keyEditable,
                    Property = p,
                    InList = isScalar && listed.Contains(p.Name),
                    ListOrder = listed.IndexOf(p.Name),
                    Edit =
                        writable && (!isKey || keyEditable)
                            ? Fields.For(p, rows.Parameter(p.Name), false, s.Field)
                            : null,
                    // A timestamp filter matches to the tick, which nobody means.
                    Filterable =
                        isScalar
                        && stored
                        && source.CanFilter(p.Name)
                        && !new[] { typeof(DateTime), typeof(DateTimeOffset) }.Contains(
                            Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType
                        ),
                    Sortable = isScalar && stored && source.CanSort(p.Name),
                }
            );
        }
        foreach (var c in _computed)
        {
            if (columns.Any(x => x.Name == c.Name))
                throw new InvalidOperationException($"{who} has two columns named '{c.Name}'.");
            var element = ScalarTypes.IsScalar(c.Type) ? null : ScalarTypes.ElementType(c.Type);
            columns.Add(
                Column(c.Name, c.Type, c.Settings) with
                {
                    Kind = element is null ? ColumnKind.Value : ColumnKind.Collection,
                    ElementType = element,
                    Compute = c.Compute,
                    InList = c.Settings.InList,
                    ListOrder = int.MaxValue,
                }
            );
        }

        if (_live.GroupBy(l => l.Id).FirstOrDefault(g => g.Count() > 1) is { } twin)
            throw new InvalidOperationException(
                $"{who} has two live widgets titled so their id is '{twin.Key}'."
            );
        var id = TableId;
        return new TableModel
        {
            Id = id,
            Title = _title,
            ClrType = typeof(T),
            Nav = _nav,
            Columns = columns,
            Key = key,
            Source = source,
            Rows = rows,
            Label = _label is { } label ? row => label((T)row) : DefaultLabel(properties, key),
            Actions = _actions,
            Related = _related.Select(r => r(ctx)).ToList(),
            Live = _live,
            Poll = _poll,
            Empty = _empty,
            Hidden = _hidden,
            DeleteWhen = _deleteWhen is { } when ? row => when((T)row) : null,
            Authorize = _authorize is { } rule ? (user, op, row) => rule(user, op, (T?)row) : null,
            Request = (user, op, row) => new TableRequest<T>(user, id, op, (T?)row),
            ActionRequest = (user, action, row) => new ActionRequest<T>(user, id, action, (T?)row),
        };
    }

    private static Func<object, string> DefaultLabel(List<PropertyInfo> properties, KeyCodec key)
    {
        var named = new[] { "Name", "Title", "Label", "DisplayName", "Email" }
            .Select(n =>
                properties.FirstOrDefault(p => p.Name == n && p.PropertyType == typeof(string))
            )
            .FirstOrDefault(p => p is not null);
        return named is null
            ? key.Encode
            : row => named.GetValue(row) as string is { Length: > 0 } s ? s : key.Encode(row);
    }

    private sealed record ComputedColumn(
        string Name,
        Type Type,
        Func<IServiceProvider, object, CancellationToken, Task<object?>> Compute,
        ColumnSettings Settings
    );
}

internal sealed class ColumnSettings
{
    public FieldSettings Field { get; } = new();
    public string? Format { get; set; }
    public Content? Content { get; set; }
    public Type? LinksToType { get; set; }
    public string? LinksToId { get; set; }
    public Dictionary<object, Tone>? Tones { get; set; }
    public bool ReadOnly { get; set; }
    public bool InList { get; set; }
}

/// <summary>One column's title, rendering and rules.</summary>
public sealed class ColumnBuilder<TProp>
{
    private readonly ColumnSettings _s;

    internal ColumnBuilder(ColumnSettings settings) => _s = settings;

    public ColumnBuilder<TProp> Title(string title)
    {
        _s.Field.Title = title;
        return this;
    }

    /// <summary>Help text beside the column's input.</summary>
    public ColumnBuilder<TProp> Description(string description)
    {
        _s.Field.Description = description;
        return this;
    }

    /// <summary>A .NET format string for the value; overrides <c>panel.Format&lt;T&gt;</c>.</summary>
    public ColumnBuilder<TProp> Format(string format)
    {
        _s.Format = format;
        return this;
    }

    /// <summary>A long value: the cell shows its first line and opens the whole of it as <paramref name="kind"/>.</summary>
    public ColumnBuilder<TProp> Content(Content kind)
    {
        _s.Content = kind;
        return this;
    }

    /// <summary>The value is a key of <typeparamref name="TTarget"/>'s table; the cell links to that row.</summary>
    public ColumnBuilder<TProp> LinksTo<TTarget>()
        where TTarget : class
    {
        _s.LinksToType = typeof(TTarget);
        return this;
    }

    /// <summary>The value is a key of the table <paramref name="tableId"/>, for a type with more than one table.</summary>
    public ColumnBuilder<TProp> LinksTo(string tableId)
    {
        _s.LinksToId = tableId;
        return this;
    }

    /// <summary>Shows the value as a status chip, coloured by value.</summary>
    public ColumnBuilder<TProp> Status(Action<StatusBuilder<TProp>> configure)
    {
        var status = new StatusBuilder<TProp>(_s.Tones ??= []);
        configure(status);
        return this;
    }

    /// <summary>A rule checked on the server for every surface; <paramref name="message"/> shows when it fails.</summary>
    public ColumnBuilder<TProp> Validate(Func<TProp, bool> rule, string message)
    {
        _s.Field.Rules.Add(v => v is TProp value && !rule(value) ? message : null);
        return this;
    }

    /// <summary>Shown but never written by the panel.</summary>
    public ColumnBuilder<TProp> ReadOnly()
    {
        _s.ReadOnly = true;
        return this;
    }

    /// <summary>Lists a computed column on the list page too: one computation per row shown.</summary>
    public ColumnBuilder<TProp> InList()
    {
        _s.InList = true;
        return this;
    }
}

/// <summary>The chip colour for each value of a status column.</summary>
public sealed class StatusBuilder<TProp>
{
    private readonly Dictionary<object, Tone> _tones;

    internal StatusBuilder(Dictionary<object, Tone> tones) => _tones = tones;

    public StatusBuilder<TProp> Tone(TProp value, Tone tone)
    {
        _tones[value!] = tone;
        return this;
    }
}

/// <summary>How a related table shows on the detail page.</summary>
public sealed class RelatedBuilder<TChild>
    where TChild : class
{
    private readonly string _foreignKey;
    private string? _title;
    private string? _table;
    private bool _inline;
    private List<string>? _columns;

    internal RelatedBuilder(string foreignKey) => _foreignKey = foreignKey;

    public RelatedBuilder<TChild> Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>The child table, when <typeparamref name="TChild"/> has more than one.</summary>
    public RelatedBuilder<TChild> Table(string tableId)
    {
        _table = tableId;
        return this;
    }

    /// <summary>Renders the child table on the detail page instead of a link to it.</summary>
    public RelatedBuilder<TChild> Inline()
    {
        _inline = true;
        return this;
    }

    /// <summary>The inline table's columns; by default the child's list columns without the foreign key.</summary>
    public RelatedBuilder<TChild> Columns(params Expression<Func<TChild, object?>>[] columns)
    {
        _columns = columns.Select(Members.Name).ToList();
        return this;
    }

    /// <summary>The inline table's columns by name, which also reaches the child's computed columns.</summary>
    public RelatedBuilder<TChild> Columns(params string[] columns)
    {
        _columns = [.. columns];
        return this;
    }

    internal RelatedModel Build(BuildContext ctx, string who) =>
        // An empty title takes the child table's own, once every table is built.
        new(
            _title ?? "",
            ctx.TableFor(typeof(TChild), _table, who),
            _foreignKey,
            _inline,
            _columns
        );
}

/// <summary>Shows a stream as a chart of two cells or as a feed, newest first.</summary>
public sealed class LiveBuilder<TPoint>
    where TPoint : notnull
{
    private List<(string, Func<object, object?>)>? _cells;
    private bool _chart;
    private int _keep = 50;

    public LiveBuilder<TPoint> Chart(
        Expression<Func<TPoint, object?>> x,
        Expression<Func<TPoint, object?>> y
    )
    {
        _cells = [Cell(x), Cell(y)];
        _chart = true;
        return this;
    }

    /// <summary>The points a chart keeps.</summary>
    public LiveBuilder<TPoint> Window(int points) => Keep(points);

    public LiveBuilder<TPoint> Feed(params Expression<Func<TPoint, object?>>[] columns)
    {
        _cells = columns.Select(Cell).ToList();
        _chart = false;
        return this;
    }

    /// <summary>The entries a feed keeps.</summary>
    public LiveBuilder<TPoint> Keep(int entries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(entries, 0);
        _keep = entries;
        return this;
    }

    internal LiveModel Build(
        string title,
        Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<object>> stream
    ) =>
        new()
        {
            Id = Members.Kebab(title),
            Title = title,
            Stream = stream,
            Cells =
                _cells
                ?? throw new InvalidOperationException(
                    $"Live '{title}' needs Chart(...) or Feed(...)."
                ),
            Chart = _chart,
            Keep = _keep,
        };

    private static (string, Func<object, object?>) Cell(Expression<Func<TPoint, object?>> e)
    {
        var read = e.Compile();
        var title = e.Body is MemberExpression or UnaryExpression { Operand: MemberExpression }
            ? Members.Humanize(Members.Name(e))
            : "Value";
        return (title, o => read((TPoint)o));
    }
}
