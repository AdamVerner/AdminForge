using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;
using AdminForge.DataAccess.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge.UI.Blazor;

/// <summary>
/// Reflection-based bridge between the Blazor renderer and the per-entity
/// <see cref="IAdminDataProvider{T}"/> registrations. Pages and components
/// depend on this interface only — they never know the entity CLR type.
/// </summary>
public sealed class BlazorUIBridge : IAdminUIBridge
{
    private readonly AdminForgeOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly DbContext _dbContext;
    private readonly IAdminAuthorizationPolicy _authzPolicy;
    private readonly IUserAccessor _userAccessor;

    // Cache compiled per-entity adapters keyed by CLR entity type.
    private readonly ConcurrentDictionary<Type, EntityAdapter> _adapters = new();

    public BlazorUIBridge(
        AdminForgeOptions options,
        IServiceProvider serviceProvider,
        DbContext dbContext,
        IAdminAuthorizationPolicy authzPolicy,
        IUserAccessor userAccessor
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(authzPolicy);
        ArgumentNullException.ThrowIfNull(userAccessor);
        _options = options;
        _serviceProvider = serviceProvider;
        _dbContext = dbContext;
        _authzPolicy = authzPolicy;
        _userAccessor = userAccessor;
    }

    public IReadOnlyList<EntityMeta> Entities => _options.Entities;
    public IReadOnlyList<DashboardMeta> Dashboards => _options.Dashboards;
    public IReadOnlyList<FormMeta> Forms => _options.Forms;

    public EntityMeta? FindEntityByRouteName(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return null;
        return _options.Entities.FirstOrDefault(e =>
            string.Equals(e.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public Task<EntityListVM> ListAsync(
        EntityMeta entity,
        ListQuery query,
        CancellationToken cancellationToken = default
    ) => GetAdapter(entity).ListAsync(query, cancellationToken);

    public Task<EntityViewVM?> FindAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    ) => GetAdapter(entity).FindAsync(encodedKey, cancellationToken);

    public Task<EntityEditVM?> LoadForEditAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    ) => GetAdapter(entity).LoadForEditAsync(encodedKey, cancellationToken);

    public EntityEditVM NewEditModel(EntityMeta entity) => GetAdapter(entity).NewEditModel();

    public async Task<string> CreateAsync(
        EntityMeta entity,
        EntityEditVM model,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Create, instance: null, cancellationToken)
            .ConfigureAwait(false);
        return await GetAdapter(entity).CreateAsync(model, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        EntityMeta entity,
        EntityEditVM model,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Update, instance: null, cancellationToken)
            .ConfigureAwait(false);
        await GetAdapter(entity).UpdateAsync(model, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        EntityMeta entity,
        string encodedKey,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Delete, instance: null, cancellationToken)
            .ConfigureAwait(false);
        return await GetAdapter(entity).DeleteAsync(encodedKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NavigationRef>> SearchRelatedAsync(
        Type relatedType,
        string? search,
        int take = 25,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(relatedType);
        if (take <= 0) take = 25;

        var meta =
            _options.Entities.FirstOrDefault(e => e.ClrType == relatedType)
            ?? throw new InvalidOperationException(
                $"Related entity '{relatedType.Name}' is not registered."
            );
        var adapter = GetAdapter(meta);
        var query = new ListQuery
        {
            Page = 0,
            PageSize = take,
            Search = search,
        };
        var list = await adapter.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var refs = new List<NavigationRef>(list.Rows.Count);
        foreach (var row in list.Rows)
        {
            // The list adapter doesn't synthesise NavigationRef for the row's own
            // identity (that's for nav columns); compose one here from the row's PK
            // and the entity's DisplayLabel (or its primary scalar value).
            var label = ResolveDisplayLabel(meta, row);
            refs.Add(new NavigationRef(row.Key, label, meta.Name));
        }
        return refs;
    }

    public async Task<NavigationRef?> FindRelatedAsync(
        Type relatedType,
        string encodedKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(relatedType);
        if (string.IsNullOrEmpty(encodedKey)) return null;
        var meta =
            _options.Entities.FirstOrDefault(e => e.ClrType == relatedType)
            ?? throw new InvalidOperationException(
                $"Related entity '{relatedType.Name}' is not registered."
            );
        var view = await GetAdapter(meta).FindAsync(encodedKey, cancellationToken).ConfigureAwait(false);
        if (view is null) return null;
        var label = ResolveDisplayLabelFromValues(meta, view.Values) ?? encodedKey;
        return new NavigationRef(view.Key, label, meta.Name);
    }

    private static string ResolveDisplayLabel(EntityMeta meta, EntityListRowVM row)
    {
        // EntityMeta.DisplayLabel takes the raw instance; row.Values has property→value
        // map (no instance). Heuristic: scan a small priority list of column names; fall
        // back to the encoded key.
        return ResolveDisplayLabelFromValues(meta, row.Values) ?? row.Key;
    }

    private static string? ResolveDisplayLabelFromValues(
        EntityMeta meta,
        IReadOnlyDictionary<string, object?> values
    )
    {
        // Mirror the preference order used by DisplayLabelResolver for entity instances:
        // Name → Title → Label → DisplayName → Email → first non-PK scalar.
        string[] preferred = ["Name", "Title", "Label", "DisplayName", "Email"];
        foreach (var name in preferred)
        {
            if (values.TryGetValue(name, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        foreach (var col in meta.Columns)
        {
            if (col.IsPrimaryKey) continue;
            if (col.Kind != ColumnKind.Scalar) continue;
            if (col.ClrType != typeof(string)) continue;
            if (values.TryGetValue(col.PropertyName, out var val) && val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        return null;
    }

    public DashboardMeta? FindDashboardByRouteName(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return null;
        return _options.Dashboards.FirstOrDefault(d =>
            string.Equals(d.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
        );
    }

    public async Task<DashboardVM> LoadDashboardAsync(
        DashboardMeta dashboard,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        // Each widget gets its own scope so scoped services (DbContext) are isolated.
        var widgets = new Dictionary<string, WidgetVM>(StringComparer.Ordinal);
        foreach (var widgetMeta in dashboard.Widgets)
        {
            try
            {
                widgets[widgetMeta.Id] = await MaterializeWidgetAsync(widgetMeta, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                widgets[widgetMeta.Id] = BuildErrorVM(widgetMeta, ex.Message);
            }
        }

        return new DashboardVM
        {
            RouteName = dashboard.RouteName,
            Title = dashboard.Title,
            Widgets = widgets,
            Layout = dashboard.Layout,
        };
    }

    private async Task<WidgetVM> MaterializeWidgetAsync(
        WidgetMeta widget,
        CancellationToken cancellationToken
    )
    {
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        switch (widget)
        {
            case StatCardMeta stat:
                var value = await stat.Fetch(sp, cancellationToken).ConfigureAwait(false);
                return new StatCardVM
                {
                    Id = stat.Id,
                    Title = stat.Title,
                    Value = value,
                    Suffix = stat.Suffix,
                };
            case LineChartMeta line:
                var rawPoints = await line.Fetch(sp, cancellationToken).ConfigureAwait(false);
                var points = new List<LineChartPoint>(rawPoints.Count);
                foreach (var item in rawPoints)
                {
                    var x = line.XSelector(item);
                    var y = line.YSelector(item);
                    double yd = 0;
                    if (y is not null)
                    {
                        try
                        {
                            yd = Convert.ToDouble(y, CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                            yd = 0;
                        }
                    }
                    points.Add(new LineChartPoint(x, yd));
                }
                return new LineChartVM
                {
                    Id = line.Id,
                    Title = line.Title,
                    Points = points,
                    XAxisLabel = line.XAxisLabel,
                    YAxisLabel = line.YAxisLabel,
                };
            case TableWidgetMeta table:
                return await MaterializeTableWidget(sp, table, cancellationToken).ConfigureAwait(false);
            default:
                throw new InvalidOperationException(
                    $"Unknown widget kind '{widget.GetType().Name}'."
                );
        }
    }

    private async Task<TableWidgetVM> MaterializeTableWidget(
        IServiceProvider scopedSp,
        TableWidgetMeta meta,
        CancellationToken cancellationToken
    )
    {
        var entityMeta =
            _options.Entities.FirstOrDefault(e => e.ClrType == meta.EntityType)
            ?? throw new InvalidOperationException(
                $"Table widget '{meta.Title}' references entity '{meta.EntityType.Name}' which is not registered."
            );

        // Build a fresh adapter using the scoped DbContext so the widget participates
        // in a request-bound transaction window. Reuses the materialisation logic
        // from the main entity-list path.
        var scopedDbContext = scopedSp.GetRequiredService<DbContext>();
        var adapter = EntityAdapter.Create(entityMeta, scopedSp, scopedDbContext, _options);

        var query = new ListQuery
        {
            Page = 0,
            PageSize = meta.MaxRows ?? 25,
            SortBy = meta.SortBy,
            SortDescending = meta.SortDescending,
        };
        var listVM = await adapter.ListAsync(query, cancellationToken).ConfigureAwait(false);

        var columns = (meta.VisibleColumns ?? entityMeta
            .Columns
            .Where(c => !c.HiddenInList && c.Kind != ColumnKind.NavigationCollection)
            .Select(c => c.PropertyName)
            .ToList())
            .ToList();

        return new TableWidgetVM
        {
            Id = meta.Id,
            Title = meta.Title,
            EntityMeta = entityMeta,
            VisibleColumns = columns,
            Rows = listVM.Rows,
        };
    }

    private static WidgetVM BuildErrorVM(WidgetMeta widget, string message) =>
        widget switch
        {
            StatCardMeta stat => new StatCardVM
            {
                Id = stat.Id,
                Title = stat.Title,
                Value = null,
                Suffix = stat.Suffix,
                Error = message,
            },
            LineChartMeta line => new LineChartVM
            {
                Id = line.Id,
                Title = line.Title,
                Points = Array.Empty<LineChartPoint>(),
                XAxisLabel = line.XAxisLabel,
                YAxisLabel = line.YAxisLabel,
                Error = message,
            },
            TableWidgetMeta table => new TableWidgetVM
            {
                Id = table.Id,
                Title = table.Title,
                EntityMeta = new EntityMeta
                {
                    ClrType = table.EntityType,
                    Name = table.EntityType.Name,
                    Label = table.EntityType.Name,
                    Columns = Array.Empty<ColumnMeta>(),
                    PrimaryKeyPropertyNames = Array.Empty<string>(),
                },
                VisibleColumns = Array.Empty<string>(),
                Rows = Array.Empty<EntityListRowVM>(),
                Error = message,
            },
            _ => throw new InvalidOperationException($"Unknown widget kind '{widget.GetType().Name}'."),
        };

    private async Task EnsureAuthorizedAsync(
        EntityMeta entity,
        AdminAction action,
        object? instance,
        CancellationToken cancellationToken
    )
    {
        var user = _userAccessor.GetUser();
        var ok = await _authzPolicy
            .IsAuthorizedAsync(entity.Name, action, user, instance, cancellationToken)
            .ConfigureAwait(false);
        if (!ok)
            throw new AdminForbiddenException(entity.Name, action);
    }

    private EntityAdapter GetAdapter(EntityMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        return _adapters.GetOrAdd(
            meta.ClrType,
            _ => EntityAdapter.Create(meta, _serviceProvider, _dbContext, _options)
        );
    }

    /// <summary>
    /// Strongly-typed reflection wrapper around <see cref="IAdminDataProvider{T}"/> for one entity type.
    /// Built once per CLR type and cached on the bridge.
    /// </summary>
    private abstract class EntityAdapter
    {
        public abstract Task<EntityListVM> ListAsync(
            ListQuery query,
            CancellationToken cancellationToken
        );
        public abstract Task<EntityViewVM?> FindAsync(
            string encodedKey,
            CancellationToken cancellationToken
        );
        public abstract Task<EntityEditVM?> LoadForEditAsync(
            string encodedKey,
            CancellationToken cancellationToken
        );
        public abstract EntityEditVM NewEditModel();
        public abstract Task<string> CreateAsync(
            EntityEditVM model,
            CancellationToken cancellationToken
        );
        public abstract Task UpdateAsync(EntityEditVM model, CancellationToken cancellationToken);
        public abstract Task<bool> DeleteAsync(
            string encodedKey,
            CancellationToken cancellationToken
        );

        public static EntityAdapter Create(
            EntityMeta meta,
            IServiceProvider sp,
            DbContext dbContext,
            AdminForgeOptions options
        )
        {
            var adapterType = typeof(GenericEntityAdapter<>).MakeGenericType(meta.ClrType);
            return (EntityAdapter)
                Activator.CreateInstance(adapterType, meta, sp, dbContext, options)!;
        }
    }

    private sealed class GenericEntityAdapter<TEntity> : EntityAdapter
        where TEntity : class
    {
        private readonly EntityMeta _meta;
        private readonly IAdminDataProvider<TEntity> _provider;
        private readonly KeyAccessor _keyAccessor;
        private readonly AdminForgeOptions _options;

        public GenericEntityAdapter(
            EntityMeta meta,
            IServiceProvider sp,
            DbContext dbContext,
            AdminForgeOptions options
        )
        {
            _meta = meta;
            _options = options;
            _provider = sp.GetRequiredService<IAdminDataProvider<TEntity>>();
            var efEntityType =
                dbContext.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException(
                    $"Entity '{typeof(TEntity).FullName}' is not part of the DbContext model."
                );
            _keyAccessor = new KeyAccessor(efEntityType);
        }

        public override async Task<EntityListVM> ListAsync(
            ListQuery query,
            CancellationToken cancellationToken
        )
        {
            var result = await _provider.ListAsync(query, cancellationToken).ConfigureAwait(false);
            var rows = new List<EntityListRowVM>(result.Items.Count);
            foreach (var item in result.Items)
            {
                rows.Add(BuildRow(item));
            }
            return new EntityListVM
            {
                EntityName = _meta.Name,
                Rows = rows,
                TotalCount = result.TotalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                SortBy = query.SortBy,
                SortDescending = query.SortDescending,
            };
        }

        public override async Task<EntityViewVM?> FindAsync(
            string encodedKey,
            CancellationToken cancellationToken
        )
        {
            var keyValues = _keyAccessor.DecodeKey(encodedKey);
            var entity = await _provider
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);
            if (entity is null)
                return null;
            var values = BuildValueMap(entity, includeNavigations: true);
            return new EntityViewVM
            {
                EntityName = _meta.Name,
                Key = _keyAccessor.EncodeKey(entity),
                Values = values,
            };
        }

        public override async Task<EntityEditVM?> LoadForEditAsync(
            string encodedKey,
            CancellationToken cancellationToken
        )
        {
            var keyValues = _keyAccessor.DecodeKey(encodedKey);
            var entity = await _provider
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);
            if (entity is null)
                return null;
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in _meta.Columns)
            {
                if (
                    column.Kind == ColumnKind.NavigationReference
                    || column.Kind == ColumnKind.NavigationCollection
                    || column.Kind == ColumnKind.Owned
                )
                    continue;
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanRead)
                    continue;
                values[column.PropertyName] = prop.GetValue(entity);
            }
            return new EntityEditVM
            {
                EntityName = _meta.Name,
                Key = _keyAccessor.EncodeKey(entity),
                Values = values,
            };
        }

        public override EntityEditVM NewEditModel()
        {
            var instance = Activator.CreateInstance<TEntity>();
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in _meta.Columns)
            {
                if (
                    column.Kind == ColumnKind.NavigationReference
                    || column.Kind == ColumnKind.NavigationCollection
                    || column.Kind == ColumnKind.Owned
                )
                    continue;
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanRead)
                    continue;
                values[column.PropertyName] = prop.GetValue(instance);
            }
            return new EntityEditVM
            {
                EntityName = _meta.Name,
                Key = null,
                Values = values,
            };
        }

        public override async Task<string> CreateAsync(
            EntityEditVM model,
            CancellationToken cancellationToken
        )
        {
            var entity = Activator.CreateInstance<TEntity>();
            ApplyValues(entity, model.Values, includePk: true);
            var created = await _provider
                .CreateAsync(entity, cancellationToken)
                .ConfigureAwait(false);
            return _keyAccessor.EncodeKey(created);
        }

        public override async Task UpdateAsync(
            EntityEditVM model,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrEmpty(model.Key))
                throw new ArgumentException("Update requires a non-empty key.", nameof(model));
            var keyValues = _keyAccessor.DecodeKey(model.Key);

            var entity = Activator.CreateInstance<TEntity>();
            // PK first
            for (var i = 0; i < _keyAccessor.KeyProperties.Count; i++)
            {
                var pkProp = _keyAccessor.KeyProperties[i].PropertyInfo;
                pkProp?.SetValue(entity, keyValues[i]);
            }
            ApplyValues(entity, model.Values, includePk: false);
            await _provider.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<bool> DeleteAsync(
            string encodedKey,
            CancellationToken cancellationToken
        )
        {
            var keyValues = _keyAccessor.DecodeKey(encodedKey);
            return await _provider.DeleteAsync(keyValues, cancellationToken).ConfigureAwait(false);
        }

        private EntityListRowVM BuildRow(TEntity entity)
        {
            var values = BuildValueMap(entity, includeNavigations: true);
            return new EntityListRowVM { Key = _keyAccessor.EncodeKey(entity), Values = values };
        }

        private Dictionary<string, object?> BuildValueMap(TEntity entity, bool includeNavigations)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in _meta.Columns)
            {
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanRead)
                    continue;

                var raw = prop.GetValue(entity);

                if (column.Kind == ColumnKind.NavigationReference)
                {
                    if (!includeNavigations || raw is null)
                    {
                        values[column.PropertyName] = null;
                        continue;
                    }
                    values[column.PropertyName] = BuildNavRef(raw, column.RelatedEntityType);
                }
                else if (column.Kind == ColumnKind.NavigationCollection)
                {
                    if (!includeNavigations || raw is not System.Collections.IEnumerable items)
                    {
                        values[column.PropertyName] = null;
                        continue;
                    }
                    var refs = new List<NavigationRef>();
                    foreach (var item in items)
                    {
                        if (item is null)
                            continue;
                        var navRef = BuildNavRef(item, column.RelatedEntityType);
                        if (navRef is not null)
                            refs.Add(navRef);
                    }
                    values[column.PropertyName] = (IReadOnlyList<NavigationRef>)refs;
                }
                else
                {
                    values[column.PropertyName] = raw;
                }
            }
            return values;
        }

        private NavigationRef? BuildNavRef(object relatedInstance, Type? relatedType)
        {
            if (relatedType is null)
                return null;
            var meta = _options.Entities.FirstOrDefault(e => e.ClrType == relatedType);
            if (meta is null)
                return null;
            var efRelated = _provider is null ? null : (IEntityTypeAccessor?)null; // fallback ignored
            // Use KeyAccessor on the related type via the EF model — borrow it from this adapter's _keyAccessor's source.
            // Simpler: use reflection over the meta.PrimaryKeyPropertyNames.
            var keyParts = new string[meta.PrimaryKeyPropertyNames.Count];
            for (var i = 0; i < meta.PrimaryKeyPropertyNames.Count; i++)
            {
                var pkName = meta.PrimaryKeyPropertyNames[i];
                var pkProp = relatedType.GetProperty(
                    pkName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                var raw = pkProp?.GetValue(relatedInstance);
                keyParts[i] = Uri.EscapeDataString(
                    Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty
                );
            }
            var encodedKey = string.Join('-', keyParts);
            var label = meta.DisplayLabel?.Invoke(relatedInstance) ?? encodedKey;
            return new NavigationRef(encodedKey, label, meta.Name);
        }

        private void ApplyValues(
            TEntity entity,
            IDictionary<string, object?> values,
            bool includePk
        )
        {
            foreach (var column in _meta.Columns)
            {
                if (
                    column.Kind == ColumnKind.NavigationReference
                    || column.Kind == ColumnKind.NavigationCollection
                    || column.Kind == ColumnKind.Owned
                )
                    continue;
                if (column.IsGenerated)
                    continue;
                if (column.IsPrimaryKey && !includePk)
                    continue;
                if (!values.TryGetValue(column.PropertyName, out var value))
                    continue;
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanWrite)
                    continue;
                prop.SetValue(entity, CoerceValue(value, prop.PropertyType));
            }
        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value is null)
                return null;
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (underlying.IsInstanceOfType(value))
                return value;
            if (underlying.IsEnum)
            {
                if (value is string s)
                    return Enum.Parse(underlying, s, ignoreCase: true);
                return Enum.ToObject(underlying, value);
            }
            if (value is string str)
            {
                if (string.IsNullOrEmpty(str) && Nullable.GetUnderlyingType(targetType) is not null)
                    return null;
                if (underlying == typeof(Guid))
                    return Guid.Parse(str);
                return Convert.ChangeType(str, underlying, CultureInfo.InvariantCulture);
            }
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }

        // Sentinel marker; never used at runtime.
        private interface IEntityTypeAccessor { }
    }
}
