using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>Configures the panel. Every page, action and form has a stable id: its route, its policy key, its audit name and its MCP tool name.</summary>
public sealed class PanelBuilder
{
    private readonly List<TableEntry> _tables = [];
    private readonly List<Func<FormModel>> _forms = [];
    private readonly List<Func<BuildContext, DashboardModel>> _dashboards = [];
    private readonly List<PageModel> _pages = [];
    private readonly Dictionary<Type, string> _formats = [];
    private readonly List<(AdminSlot, Type)> _slots = [];
    private readonly ThemeOptions _theme = new();
    private string _title = "Admin";
    private string? _welcome;
    private (string, AdminEnvironment)? _environment;
    private string? _signOut;
    private string? _policy;
    private bool _anonymous;
    private string? _group;
    private int _order;

    internal PanelBuilder(IServiceCollection services) => Services = services;

    internal IServiceCollection Services { get; }

    /// <summary>Mapped by <c>MapAdminForge()</c>, after the panel's own endpoints are known to need them.</summary>
    internal List<Action<IEndpointRouteBuilder, PanelModel>> Endpoints { get; } = [];

    /// <summary>Describes a row type the way its store sees it; set by a data package such as EF Core.</summary>
    internal Func<IServiceProvider, Type, EntityShape?>? Shapes { get; set; }

    /// <summary>The source of a table whose type the data package serves and that names none.</summary>
    internal Func<Type, SourceModel>? DefaultSource { get; set; }

    public PanelBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>The home page's text above its dashboard.</summary>
    public PanelBuilder Welcome(string message)
    {
        _welcome = message;
        return this;
    }

    public PanelBuilder Environment(string label, AdminEnvironment kind)
    {
        _environment = (label, kind);
        return this;
    }

    public PanelBuilder Theme(Action<ThemeOptions> configure)
    {
        configure(_theme);
        if (_theme.PrimaryColor is { } color && !UI.Theme.IsHex(color))
            throw new InvalidOperationException(
                $"Theme PrimaryColor '{color}' is not a #RRGGBB colour."
            );
        return this;
    }

    /// <summary>
    /// Renders a host component in <paramref name="slot"/>. A detail slot shows it on every detail
    /// page and hands it the page's <c>TableId</c> and <c>Key</c> as string parameters.
    /// </summary>
    public PanelBuilder Render<TComponent>(AdminSlot slot)
        where TComponent : IComponent
    {
        if (
            slot is AdminSlot.AboveDetail or AdminSlot.BelowDetail
            && new[] { "TableId", "Key" }.FirstOrDefault(p =>
                typeof(TComponent).GetProperty(p) is not { } property
                || property.PropertyType != typeof(string)
                || !property.IsDefined(typeof(ParameterAttribute), true)
            )
                is { } missing
        )
            throw new InvalidOperationException(
                $"{typeof(TComponent).Name} renders in {slot}, so it needs a [Parameter] public string {missing} {{ get; set; }}."
            );
        _slots.Add((slot, typeof(TComponent)));
        return this;
    }

    /// <summary>Shows a sign-out button that posts to <paramref name="path"/>, an endpoint the host maps.</summary>
    public PanelBuilder SignOut(string path)
    {
        _signOut = path;
        return this;
    }

    /// <summary>The umbrella policy, required on the panel's endpoints and evaluated again on every operation.</summary>
    /// <remarks>On the endpoints the host's scheme issues the challenge: a cookie scheme redirects, a bearer scheme answers 401.</remarks>
    public PanelBuilder RequireAuthorization(string policy)
    {
        if (_policy is not null && _policy != policy)
            throw new InvalidOperationException(
                $"The panel already requires '{_policy}'; it cannot also require '{policy}'. UseLogin sets its own policy."
            );
        _policy = policy;
        return this;
    }

    /// <summary>Declares the panel deliberately open. For demos, local tooling and tests.</summary>
    public PanelBuilder AllowAnonymous()
    {
        _anonymous = true;
        return this;
    }

    /// <summary>Asked on every operation after the umbrella policy; resolved from the operation's scope.</summary>
    public PanelBuilder Policy<TPolicy>()
        where TPolicy : class, IAdminPolicy
    {
        Services.AddScoped<IAdminPolicy, TPolicy>();
        return this;
    }

    /// <summary>Receives every successful write, action and form submission.</summary>
    public PanelBuilder Audit<TSink>()
        where TSink : class, IAdminAuditSink
    {
        Services.AddScoped<IAdminAuditSink, TSink>();
        return this;
    }

    /// <summary>The format every column of type <typeparamref name="T"/> renders with, unless it names its own.</summary>
    public PanelBuilder Format<T>(string format)
    {
        _formats[typeof(T)] = format;
        return this;
    }

    /// <summary>The panel's own sign-in over <typeparamref name="TValidator"/>: a cookie, a login page at <c>/admin/login</c>, and sign-out.</summary>
    /// <remarks>
    /// Its signed-in policy becomes the umbrella policy. <paramref name="endpoints"/> runs on the login
    /// page, sign-in and sign-out endpoints, such as to add an endpoint filter or a rate limit.
    /// </remarks>
    public PanelBuilder UseLogin<TValidator>(
        Action<CookieAuthenticationOptions>? configure = null,
        Action<IEndpointConventionBuilder>? endpoints = null
    )
        where TValidator : class, IAdminCredentialValidator
    {
        Login.Register<TValidator>(this, configure, endpoints);
        RequireAuthorization(Login.Policy);
        SignOut(Login.LogoutPath);
        return this;
    }

    /// <summary>Files the registrations in <paramref name="configure"/> under one sidebar group.</summary>
    public PanelBuilder Group(string title, Action<PanelBuilder> configure)
    {
        if (_group is not null)
            throw new InvalidOperationException(
                $"Group '{title}' is inside group '{_group}'; groups do not nest."
            );
        _group = title;
        configure(this);
        _group = null;
        return this;
    }

    /// <summary>A table of <typeparamref name="T"/>. Its id defaults to the kebab-case type name.</summary>
    public PanelBuilder Table<T>(Action<TableBuilder<T>>? configure = null)
        where T : class
    {
        var table = new TableBuilder<T>(Nav());
        configure?.Invoke(table);
        _tables.Add(new TableEntry(table.TableId, typeof(T), table.Build));
        return this;
    }

    /// <summary>A form whose fields are <typeparamref name="TInput"/>'s properties.</summary>
    public PanelBuilder Form<TInput>(string id, Action<FormBuilder<TInput>> configure)
        where TInput : class
    {
        var form = new FormBuilder<TInput>(id, Nav());
        configure(form);
        _forms.Add(form.Build);
        return this;
    }

    public PanelBuilder Dashboard(string id, Action<DashboardBuilder> configure)
    {
        var dashboard = new DashboardBuilder(id, Nav());
        configure(dashboard);
        _dashboards.Add(dashboard.Build);
        return this;
    }

    /// <summary>A Razor page of the host's own, inside the shell. Its <c>@page</c> route must start with <c>/admin/</c>.</summary>
    public PanelBuilder Page<TComponent>(Action<PageBuilder>? configure = null)
        where TComponent : IComponent
    {
        var page = new PageBuilder(typeof(TComponent), Nav());
        configure?.Invoke(page);
        _pages.Add(page.Build());
        return this;
    }

    private NavModel Nav() => new(_group, NavArea.Main, false, _order++);

    internal PanelModel Build(IServiceProvider services)
    {
        Unique(_tables.Select(t => t.Id), "table");
        var context = new BuildContext(services, _tables, this);
        var tables = _tables.Select(t => t.Build(context)).ToList();
        // A key on text must not match "acme-labs" for "acme", nor "10" for "1".
        tables = tables
            .Select(t =>
                t with
                {
                    Exact = tables
                        .SelectMany(p => p.Related)
                        .Where(r => r.TableId == t.Id)
                        .Select(r => r.ForeignKey)
                        .Concat(t.Columns.Where(c => c.LinksTo is not null).Select(c => c.Name))
                        .ToHashSet(),
                    Related = t
                        .Related.Select(r =>
                            r.Title.Length > 0
                                ? r
                                : r with
                                {
                                    Title = tables.First(c => c.Id == r.TableId).Title,
                                }
                        )
                        .ToList(),
                }
            )
            .ToList();
        var forms = _forms.Select(f => f()).ToList();
        var dashboards = _dashboards.Select(d => d(context)).ToList();
        Unique(forms.Select(f => f.Id), "form");
        Unique(dashboards.Select(d => d.Id), "dashboard");
        if (dashboards.Count(d => d.Home) > 1)
            throw new InvalidOperationException(
                $"Dashboards {string.Join(", ", dashboards.Where(d => d.Home).Select(d => d.Id))} all call Home(); one home page."
            );
        foreach (var table in tables)
        foreach (var related in table.Related)
        {
            var child = tables.First(t => t.Id == related.TableId);
            if (child.Column(related.ForeignKey) is not { Filterable: true })
                throw new InvalidOperationException(
                    child.Column(related.ForeignKey) is null
                        ? $"Table '{table.Id}' relates '{child.Id}' by {related.ForeignKey}, which '{child.Id}' hides. Take it out of Hide(...)."
                        : $"Table '{table.Id}' relates '{child.Id}' by {related.ForeignKey}, which its source does not filter on. Declare it with s.Filters(x => x.{related.ForeignKey})."
                );
            if (table.Key.Properties.Count != 1)
                throw new InvalidOperationException(
                    $"Table '{table.Id}' has a composite key, so Related cannot pin a child to it."
                );
            if (related.Columns?.FirstOrDefault(c => child.Column(c) is null) is { } unknown)
                throw new InvalidOperationException(
                    $"Table '{table.Id}' shows '{child.Id}' with a column '{unknown}', which '{child.Id}' does not have."
                );
        }
        foreach (var table in tables)
        foreach (var column in table.Columns)
            if (
                column.LinksTo is { } target
                && tables.First(t => t.Id == target).Key.Properties.Count != 1
            )
                throw new InvalidOperationException(
                    $"Table '{table.Id}' column {column.Name} links to '{target}', which has a composite key; a link holds one value."
                );
        return new PanelModel
        {
            Title = _title,
            Welcome = _welcome,
            Environment = _environment,
            Theme = _theme,
            SignOutPath = _signOut,
            Policy = _policy,
            AllowAnonymous = _anonymous,
            Tables = tables,
            Forms = forms,
            Dashboards = dashboards,
            Pages = _pages,
            Formats = _formats,
            Slots = _slots,
        };
    }

    private static void Unique(IEnumerable<string> ids, string kind)
    {
        var duplicate = ids.GroupBy(i => i, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Two of the panel's {kind}s have the id '{duplicate.Key}'; give one another with Id(...)."
            );
    }
}

internal sealed record TableEntry(string Id, Type ClrType, Func<BuildContext, TableModel> Build);

/// <summary>A row type as its store describes it: its key, the properties it generates, the ones that are not columns.</summary>
internal sealed record EntityShape(
    IReadOnlyList<string> Keys,
    IReadOnlySet<string> Generated,
    IReadOnlySet<string> NotColumns
);

internal sealed class BuildContext(
    IServiceProvider services,
    IReadOnlyList<TableEntry> tables,
    PanelBuilder panel
)
{
    public IServiceProvider Services => services;

    public EntityShape? Shape(Type type) => panel.Shapes?.Invoke(services, type);

    public SourceModel? DefaultSource(Type type) => panel.DefaultSource?.Invoke(type);

    /// <summary>The one table of <paramref name="type"/>, or the table named <paramref name="id"/>; a null type takes any table.</summary>
    public string TableFor(Type? type, string? id, string who)
    {
        if (id is not null)
        {
            var named =
                tables.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"{who} names table '{id}', which is not registered."
                );
            if (type is not null && named.ClrType != type)
                throw new InvalidOperationException(
                    $"{who} names table '{id}', which lists {named.ClrType.Name} rows, not {type.Name}."
                );
            return named.Id;
        }
        ArgumentNullException.ThrowIfNull(type);
        var matches = tables.Where(t => t.ClrType == type).ToList();
        return matches.Count switch
        {
            1 => matches[0].Id,
            0 => throw new InvalidOperationException(
                $"{who} points at {type.Name}, which has no table. Register it with panel.Table<{type.Name}>()."
            ),
            _ => throw new InvalidOperationException(
                $"{who} points at {type.Name}, which has {matches.Count} tables ({string.Join(", ", matches.Select(m => m.Id))}). Name one by its id: r.Table(id), c.LinksTo(id) or w.Table(id)."
            ),
        };
    }
}
