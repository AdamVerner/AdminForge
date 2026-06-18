using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;

namespace AdminForge.Core.Configuration;

/// <summary>
/// Top-level fluent builder for the AdminForge configuration surface.
/// Lives in <c>Core</c> so it has no Blazor / ASP.NET dependencies — the
/// composition root in the meta-package owns wiring this into DI.
/// </summary>
public sealed class AdminForgeBuilder
{
    private readonly Dictionary<Type, EntityMeta> _scannedMetaByType;
    private readonly List<EntityMeta> _registeredEntities = [];
    private readonly List<DashboardMeta> _dashboards = [];
    private readonly List<FormMeta> _forms = [];

    /// <summary>Mutable surface for the top-level options (route prefix, title).</summary>
    public AdminForgeOptionsDraft Options { get; } = new();

    /// <summary>The audit sink registered via <see cref="WithAuditLog"/>, if any.</summary>
    public IAuditSink? AuditSink { get; private set; }

    /// <summary>
    /// Mutable theme surface for the admin shell. Configured via
    /// <see cref="WithTheme(System.Action{ThemeOptions})"/>; defaults render the
    /// renderer's stock palette and no logo.
    /// </summary>
    public ThemeOptions Theme { get; } = new();

    /// <summary>
    /// Constructs a builder seeded with the entity metadata produced by the
    /// reflection scanner. Lookups in <see cref="AddTable{T}"/> resolve against this map.
    /// </summary>
    public AdminForgeBuilder(IReadOnlyList<EntityMeta> scannedEntities)
    {
        ArgumentNullException.ThrowIfNull(scannedEntities);
        _scannedMetaByType = scannedEntities.ToDictionary(m => m.ClrType);
    }

    /// <summary>Convenience overload for hosts with no scanner output (tests, dashboards-only).</summary>
    public AdminForgeBuilder()
        : this([]) { }

    /// <summary>Set the display title shown in the admin shell.</summary>
    public AdminForgeBuilder WithTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Options.Title = title;
        return this;
    }

    /// <summary>
    /// Require the supplied policy on every admin endpoint. Per-entity, per-action
    /// policies still apply on top.
    /// </summary>
    public AdminForgeBuilder RequireAuthorizationPolicy(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        Options.AuthorizationPolicy = policyName;
        return this;
    }

    /// <summary>
    /// Registers an entity table page. The entity must have been discovered by the
    /// reflection scanner (i.e. it must be a <c>DbSet</c> on the host's <c>DbContext</c>).
    /// </summary>
    public AdminForgeBuilder AddTable<T>(Action<EntityBuilder<T>>? configure = null)
        where T : class
    {
        if (!_scannedMetaByType.TryGetValue(typeof(T), out var meta))
        {
            throw new InvalidOperationException(
                $"Entity '{typeof(T).FullName}' was not discovered by the reflection scanner. "
                    + "Ensure it is exposed as a DbSet on the registered DbContext."
            );
        }

        if (_registeredEntities.Contains(meta))
        {
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' is already registered."
            );
        }

        var entityBuilder = new EntityBuilder<T>(meta);
        configure?.Invoke(entityBuilder);

        // Fill in default DisplayLabel if user did not override.
        meta.DisplayLabel ??= DisplayLabelResolver.Build(
            meta.ClrType,
            meta.PrimaryKeyPropertyNames
        );

        _registeredEntities.Add(meta);
        return this;
    }

    /// <summary>
    /// Registers an entity using already-built <see cref="EntityMeta"/> (escape hatch for
    /// custom providers that don't go through EF reflection).
    /// </summary>
    /// <remarks>
    /// Because the entity has no EF-backed <c>DbSet</c>, the default
    /// <c>HostScopedDataProvider&lt;T&gt;</c> will throw when AdminForge tries to serve its
    /// list or view pages. You must register a concrete
    /// <see cref="AdminForge.Core.Contracts.IAdminDataProvider{T}"/> for the same CLR type
    /// before the open-generic fallback is reached — use
    /// <c>services.AddAdminForgeDataProvider&lt;TEntity, TProvider&gt;()</c> (or the raw DI
    /// overload) in your host's <c>Program.cs</c>.
    /// </remarks>
    public AdminForgeBuilder AddTable(EntityMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        if (_registeredEntities.Any(m => m.ClrType == meta.ClrType))
        {
            throw new InvalidOperationException(
                $"Entity '{meta.ClrType.Name}' is already registered."
            );
        }
        meta.DisplayLabel ??= DisplayLabelResolver.Build(
            meta.ClrType,
            meta.PrimaryKeyPropertyNames
        );
        _registeredEntities.Add(meta);
        return this;
    }

    /// <summary>
    /// Registers a dashboard page. The <paramref name="routeName"/> doubles as the
    /// URL segment (<c>/admin/dashboards/{routeName}</c>) and as the registry key —
    /// it must be unique across registered dashboards. The supplied <paramref name="configure"/>
    /// callback composes widgets and (optionally) a row-based layout.
    /// </summary>
    public AdminForgeBuilder AddDashboard(string routeName, Action<DashboardBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(configure);

        if (
            _dashboards.Any(d =>
                string.Equals(d.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException($"Dashboard '{routeName}' is already registered.");
        }

        var dashboardBuilder = new DashboardBuilder(routeName);
        configure(dashboardBuilder);
        var meta = dashboardBuilder.Build();

        // Default nav label to the dashboard title when the user didn't supply one.
        meta.Nav.Label ??= meta.Title;

        _dashboards.Add(meta);
        return this;
    }

    /// <summary>
    /// Registers a generic form. The <paramref name="routeName"/> doubles as the
    /// URL segment (<c>/admin/forms/{routeName}</c>) and as the registry key —
    /// it must be unique across registered forms. The callback declares fields
    /// and supplies a submit handler.
    /// </summary>
    public AdminForgeBuilder AddForm(string routeName, Action<FormBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(configure);

        if (
            _forms.Any(f =>
                string.Equals(f.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            throw new InvalidOperationException($"Form '{routeName}' is already registered.");
        }

        var formBuilder = new FormBuilder(routeName);
        configure(formBuilder);
        var meta = formBuilder.Build();

        if (meta.Submit is null)
            throw new InvalidOperationException(
                $"Form '{routeName}' must register a submit handler via OnSubmit(...)."
            );

        // Default nav label to the form title when the user didn't supply one.
        meta.Nav.Label ??= meta.Title;

        _forms.Add(meta);
        return this;
    }

    /// <summary>
    /// Registers an audit sink. Every mutating admin action will invoke this sink
    /// before returning a successful response to the user.
    /// </summary>
    public AdminForgeBuilder WithAuditLog(IAuditSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        AuditSink = sink;
        return this;
    }

    /// <summary>Convenience overload for inline delegate-style audit sinks.</summary>
    public AdminForgeBuilder WithAuditLog(Func<AuditEvent, CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        AuditSink = new DelegateAuditSink(callback);
        return this;
    }

    /// <summary>
    /// Customise the admin shell's visual theme (logo, primary / secondary palette).
    /// All values are optional — anything left unset keeps the renderer's defaults.
    /// </summary>
    public AdminForgeBuilder WithTheme(Action<ThemeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Theme);
        return this;
    }

    /// <summary>Materialise the immutable <see cref="AdminForgeOptions"/> consumed by the host pipeline.</summary>
    public AdminForgeOptions Build() =>
        new()
        {
            RoutePrefix = Options.RoutePrefix,
            Title = Options.Title,
            AuthorizationPolicy = Options.AuthorizationPolicy,
            Entities = _registeredEntities.AsReadOnly(),
            Dashboards = _dashboards.AsReadOnly(),
            Forms = _forms.AsReadOnly(),
            AuditSink = AuditSink,
            Theme = new ThemeOptions
            {
                LogoUrl = Theme.LogoUrl,
                LogoAlt = Theme.LogoAlt,
                PrimaryColor = Theme.PrimaryColor,
                SecondaryColor = Theme.SecondaryColor,
            },
        };

    private sealed class DelegateAuditSink(Func<AuditEvent, CancellationToken, Task> callback)
        : IAuditSink
    {
        public Task RecordAsync(
            AuditEvent auditEvent,
            CancellationToken cancellationToken = default
        ) => callback(auditEvent, cancellationToken);
    }
}

/// <summary>
/// Mutable scratch surface used by the fluent builder before <see cref="AdminForgeBuilder.Build"/>
/// freezes the final <see cref="AdminForgeOptions"/>.
/// </summary>
public sealed class AdminForgeOptionsDraft
{
    /// <inheritdoc cref="AdminForgeOptions.RoutePrefix" />
    public string RoutePrefix { get; set; } = "admin";

    /// <inheritdoc cref="AdminForgeOptions.Title" />
    public string Title { get; set; } = "Admin";

    /// <inheritdoc cref="AdminForgeOptions.AuthorizationPolicy" />
    public string? AuthorizationPolicy { get; set; }
}
