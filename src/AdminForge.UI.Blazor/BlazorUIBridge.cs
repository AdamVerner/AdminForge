using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AdminForge.Core.Configuration;
using AdminForge.Core.Contracts;
using AdminForge.Core.LiveUpdates;
using AdminForge.Core.Metadata;
using AdminForge.Core.ViewModels;
using AdminForge.DataAccess.EfCore;
using AdminForge.LiveUpdates;
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
    private readonly ILiveSourceRegistry? _liveRegistry;

    // Cache compiled per-entity adapters keyed by CLR entity type.
    private readonly ConcurrentDictionary<Type, EntityAdapter> _adapters = new();

    public BlazorUIBridge(
        AdminForgeOptions options,
        IServiceProvider serviceProvider,
        DbContext dbContext,
        IAdminAuthorizationPolicy authzPolicy,
        IUserAccessor userAccessor,
        ILiveSourceRegistry? liveRegistry = null
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
        _liveRegistry = liveRegistry;
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
        IActionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Create, instance: null, cancellationToken)
            .ConfigureAwait(false);

        var adapter = GetAdapter(entity);

        // No custom handler → preserve the legacy data-provider path verbatim.
        if (entity.CustomCreateHandler is null)
        {
            return await adapter.CreateAsync(model, cancellationToken).ConfigureAwait(false);
        }

        // Custom handler: materialise the typed entity from the form values (same code
        // path the legacy CreateAsync uses internally), invoke the handler in a fresh
        // DI scope, then dispatch on the result. Audit is emitted here because the
        // data provider — which normally fires Create audit — is bypassed entirely.
        var instance = adapter.MaterializeFromVM(model);
        var actionContext = context ?? new NullActionContext();

        CreateResult result;
        using (var scope = _serviceProvider.CreateScope())
        {
            result = await entity
                .CustomCreateHandler(
                    scope.ServiceProvider,
                    instance,
                    actionContext,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        switch (result)
        {
            case CreateResult.Success success:
            {
                var encodedKey = Uri.EscapeDataString(
                    Convert.ToString(success.Id, CultureInfo.InvariantCulture) ?? string.Empty
                );
                if (_options.AuditSink is not null)
                {
                    var snapshot = adapter.SnapshotScalarValues(instance);
                    await _options
                        .AuditSink.RecordAsync(
                            new AuditEvent
                            {
                                EntityType = entity.Name,
                                Action = AuditAction.Create,
                                EntityId = encodedKey,
                                ChangedValues = snapshot.ToDictionary(
                                    kvp => kvp.Key,
                                    kvp => new AuditValueChange(null, kvp.Value)
                                ),
                                User = _userAccessor.GetUserId(),
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                return encodedKey;
            }
            case CreateResult.Failure failure:
                throw new EntityCreateFailedException(entity.Name, failure.Message);
            default:
                throw new InvalidOperationException(
                    $"Unknown CreateResult variant '{result.GetType().Name}'."
                );
        }
    }

    public async Task UpdateAsync(
        EntityMeta entity,
        EntityEditVM model,
        IActionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Update, instance: null, cancellationToken)
            .ConfigureAwait(false);

        var adapter = GetAdapter(entity);

        // No custom handler → preserve the legacy data-provider path verbatim.
        if (entity.CustomUpdateHandler is null)
        {
            await adapter.UpdateAsync(model, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(model.Key))
            throw new ArgumentException("Update requires a non-empty key.", nameof(model));

        // Custom handler: load the original from the data provider, materialise the
        // patched instance from the form, snapshot before-state, then dispatch.
        // Audit is emitted here because the data provider — which normally fires
        // Update audit — is bypassed entirely.
        var original = await adapter
            .LoadRawAsync(model.Key, cancellationToken)
            .ConfigureAwait(false);
        if (original is null)
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' with key '{model.Key}' was not found."
            );

        var before = adapter.SnapshotScalarValues(original);
        var patched = adapter.MaterializePatched(original, model);
        var actionContext = context ?? new NullActionContext();

        UpdateResult result;
        using (var scope = _serviceProvider.CreateScope())
        {
            result = await entity
                .CustomUpdateHandler(
                    scope.ServiceProvider,
                    original,
                    patched,
                    actionContext,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        switch (result)
        {
            case UpdateResult.Success:
            {
                if (_options.AuditSink is not null)
                {
                    var after = adapter.SnapshotScalarValues(patched);
                    var changes = new Dictionary<string, AuditValueChange>(StringComparer.Ordinal);
                    foreach (var (key, newVal) in after)
                    {
                        before.TryGetValue(key, out var oldVal);
                        if (!Equals(oldVal, newVal))
                            changes[key] = new AuditValueChange(oldVal, newVal);
                    }
                    await _options
                        .AuditSink.RecordAsync(
                            new AuditEvent
                            {
                                EntityType = entity.Name,
                                Action = AuditAction.Update,
                                EntityId = model.Key,
                                ChangedValues = changes,
                                User = _userAccessor.GetUserId(),
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                return;
            }
            case UpdateResult.Failure failure:
                throw new EntityUpdateFailedException(entity.Name, failure.Message);
            default:
                throw new InvalidOperationException(
                    $"Unknown UpdateResult variant '{result.GetType().Name}'."
                );
        }
    }

    public async Task<bool> DeleteAsync(
        EntityMeta entity,
        string encodedKey,
        IActionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureAuthorizedAsync(entity, AdminAction.Delete, instance: null, cancellationToken)
            .ConfigureAwait(false);

        if (entity.CustomDeleteHandler is null)
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' has no delete handler. Register one via EntityBuilder<T>.OnDelete(...)."
            );

        var adapter = GetAdapter(entity);
        var instance = await adapter
            .LoadRawAsync(encodedKey, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
            return false;

        var actionContext = context ?? new NullActionContext();
        DeleteResult result;
        using (var scope = _serviceProvider.CreateScope())
        {
            result = await entity
                .CustomDeleteHandler(
                    scope.ServiceProvider,
                    instance,
                    actionContext,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        switch (result)
        {
            case DeleteResult.Success:
            {
                if (_options.AuditSink is not null)
                {
                    await _options
                        .AuditSink.RecordAsync(
                            new AuditEvent
                            {
                                EntityType = entity.Name,
                                Action = AuditAction.Delete,
                                EntityId = encodedKey,
                                User = _userAccessor.GetUserId(),
                            },
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
                return true;
            }
            case DeleteResult.Failure failure:
                throw new EntityDeleteFailedException(entity.Name, failure.Message);
            default:
                throw new InvalidOperationException(
                    $"Unknown DeleteResult variant '{result.GetType().Name}'."
                );
        }
    }

    public async Task<IReadOnlyList<NavigationRef>> SearchRelatedAsync(
        Type relatedType,
        string? search,
        int take = 25,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(relatedType);
        if (take <= 0)
            take = 25;

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
        if (string.IsNullOrEmpty(encodedKey))
            return null;
        var meta =
            _options.Entities.FirstOrDefault(e => e.ClrType == relatedType)
            ?? throw new InvalidOperationException(
                $"Related entity '{relatedType.Name}' is not registered."
            );
        var view = await GetAdapter(meta)
            .FindAsync(encodedKey, cancellationToken)
            .ConfigureAwait(false);
        if (view is null)
            return null;
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
            if (
                values.TryGetValue(name, out var val)
                && val is string s
                && !string.IsNullOrWhiteSpace(s)
            )
                return s;
        }
        foreach (var col in meta.Columns)
        {
            if (col.IsPrimaryKey)
                continue;
            if (col.Kind != ColumnKind.Scalar)
                continue;
            if (col.ClrType != typeof(string))
                continue;
            if (
                values.TryGetValue(col.PropertyName, out var val)
                && val is string s
                && !string.IsNullOrWhiteSpace(s)
            )
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

    public async Task InvokeActionAsync(
        string entityRouteName,
        string encodedKey,
        string actionName,
        IActionContext context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityRouteName);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentNullException.ThrowIfNull(context);

        var entity =
            FindEntityByRouteName(entityRouteName)
            ?? throw new InvalidOperationException($"Unknown entity '{entityRouteName}'.");
        var action =
            entity.Actions.FirstOrDefault(a =>
                string.Equals(a.Name, actionName, StringComparison.Ordinal)
            )
            ?? throw new InvalidOperationException(
                $"Action '{actionName}' is not registered on entity '{entity.Name}'."
            );

        var adapter = GetAdapter(entity);
        var instance = await adapter
            .LoadRawAsync(encodedKey, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' with key '{encodedKey}' was not found."
            );

        // Per-action authorization carries the action name so policies can branch on it.
        await EnsureAuthorizedAsync(
                entity,
                AdminAction.Custom,
                instance,
                cancellationToken,
                actionName
            )
            .ConfigureAwait(false);

        // Invoke inside a fresh DI scope so the handler can resolve scoped services
        // (e.g. its own DbContext) without entangling with the bridge's request scope.
        using (var scope = _serviceProvider.CreateScope())
        {
            await action.Handler(scope.ServiceProvider, instance, context).ConfigureAwait(false);
        }

        if (_options.AuditSink is not null)
        {
            await _options
                .AuditSink.RecordAsync(
                    new AuditEvent
                    {
                        EntityType = entity.Name,
                        Action = AuditAction.Custom,
                        EntityId = encodedKey,
                        ChangedValues = new Dictionary<string, AuditValueChange>(
                            StringComparer.Ordinal
                        )
                        {
                            ["ActionName"] = new AuditValueChange(null, actionName),
                        },
                        User = _userAccessor.GetUserId(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    public IAsyncEnumerable<LiveUpdate<LineChartPoint>>? SubscribeLineChart(
        DashboardMeta dashboard,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        if (_liveRegistry is null)
            return null;
        var widget = dashboard
            .Widgets.OfType<LineChartMeta>()
            .FirstOrDefault(w => string.Equals(w.Id, widgetId, StringComparison.Ordinal));
        if (widget?.LiveDataSource is null)
            return null;
        return SubscribeLineChartCore(dashboard, widget, _liveRegistry, cancellationToken);
    }

    private static async IAsyncEnumerable<LiveUpdate<LineChartPoint>> SubscribeLineChartCore(
        DashboardMeta dashboard,
        LineChartMeta widget,
        ILiveSourceRegistry registry,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var name = $"widget:{dashboard.RouteName}:{widget.Id}";
        var sourceObj = registry.GetOrCreate(name, widget.LiveDataSource!);
        var subscribeMethod = sourceObj
            .GetType()
            .GetMethod(nameof(ILiveDataSource<object>.Subscribe))!;
        var enumerable = subscribeMethod.Invoke(sourceObj, new object?[] { cancellationToken })!;

        // Project each TPoint to a LineChartPoint via the widget's X/Y selectors.
        var elementType = widget.LiveDataSource!.ItemType;
        var liveUpdateType = typeof(LiveUpdate<>).MakeGenericType(elementType);
        var asyncEnumerableType = typeof(IAsyncEnumerable<>).MakeGenericType(liveUpdateType);
        var castMethod = typeof(BlazorUIBridge)
            .GetMethod(
                nameof(EnumerateAsLineChartPoints),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
            )!
            .MakeGenericMethod(elementType);
        var projected =
            (IAsyncEnumerable<LiveUpdate<LineChartPoint>>)
                castMethod.Invoke(
                    null,
                    new object?[]
                    {
                        enumerable,
                        widget.XSelector,
                        widget.YSelector,
                        cancellationToken,
                    }
                )!;
        await foreach (var u in projected.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return u;
        }
    }

    private static async IAsyncEnumerable<
        LiveUpdate<LineChartPoint>
    > EnumerateAsLineChartPoints<TPoint>(
        IAsyncEnumerable<LiveUpdate<TPoint>> source,
        Func<object, object?> xSelector,
        Func<object, object?> ySelector,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (
            var update in source.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            var projected = new LineChartPoint[update.Items.Count];
            for (var i = 0; i < update.Items.Count; i++)
            {
                var item = update.Items[i]!;
                var x = xSelector(item!);
                var y = ConvertToDouble(ySelector(item!));
                projected[i] = new LineChartPoint(x, y);
            }
            yield return new LiveUpdate<LineChartPoint>(update.Kind, projected, update.Timestamp);
        }
    }

    private static double ConvertToDouble(object? value)
    {
        if (value is null)
            return 0d;
        if (value is double d)
            return d;
        try
        {
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0d;
        }
    }

    public IReadOnlyList<FormSummary> ListForms()
    {
        var summaries = new List<FormSummary>(_options.Forms.Count);
        foreach (var f in _options.Forms)
            summaries.Add(new FormSummary(f.RouteName, f.Title, f.Nav));
        return summaries;
    }

    public FormVM? GetForm(string routeName)
    {
        if (string.IsNullOrWhiteSpace(routeName))
            return null;
        var meta = _options.Forms.FirstOrDefault(f =>
            string.Equals(f.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
        );
        if (meta is null)
            return null;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in meta.Fields)
        {
            values[field.Name] = field.Kind switch
            {
                FieldKind.Bool => false,
                _ => null,
            };
        }
        return new FormVM
        {
            RouteName = meta.RouteName,
            Title = meta.Title,
            Description = meta.Description,
            Fields = meta.Fields.AsReadOnly(),
            Values = values,
        };
    }

    public async Task SubmitFormAsync(
        string routeName,
        FormSubmission submission,
        IActionContext? context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(submission);

        var meta =
            _options.Forms.FirstOrDefault(f =>
                string.Equals(f.RouteName, routeName, StringComparison.OrdinalIgnoreCase)
            ) ?? throw new InvalidOperationException($"Form '{routeName}' is not registered.");
        if (meta.Submit is null)
            throw new InvalidOperationException(
                $"Form '{routeName}' has no registered submit handler."
            );

        // Authorize before validating — denial should short-circuit work.
        var entityNameForAuthz = $"Form:{routeName}";
        var user = _userAccessor.GetUser();
        var authorized = await _authzPolicy
            .IsAuthorizedAsync(
                entityNameForAuthz,
                AdminAction.FormSubmit,
                user,
                instance: null,
                actionName: routeName,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (!authorized)
            throw new AdminForbiddenException(entityNameForAuthz, AdminAction.FormSubmit);

        // Validate (Required + per-field validators).
        var errors = ValidateSubmission(meta, submission);
        if (errors.Count > 0)
            throw new FormValidationException(routeName, errors);

        // Invoke handler in a fresh DI scope.
        using (var scope = _serviceProvider.CreateScope())
        {
            var actionContext = context ?? new NullActionContext();
            await meta.Submit(scope.ServiceProvider, submission, actionContext)
                .ConfigureAwait(false);
        }

        // Audit.
        if (_options.AuditSink is not null)
        {
            var changes = new Dictionary<string, AuditValueChange>(StringComparer.Ordinal);
            foreach (var field in meta.Fields)
            {
                if (field.Kind == FieldKind.FileUpload)
                {
                    submission.Files.TryGetValue(field.Name, out var file);
                    if (file is null)
                    {
                        changes[field.Name] = new AuditValueChange(null, null);
                    }
                    else
                    {
                        var summary = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["FileName"] = file.FileName,
                            ["ContentType"] = file.ContentType,
                            ["Length"] = file.Length,
                        };
                        changes[field.Name] = new AuditValueChange(null, summary);
                    }
                }
                else
                {
                    submission.Values.TryGetValue(field.Name, out var v);
                    changes[field.Name] = new AuditValueChange(null, v);
                }
            }
            await _options
                .AuditSink.RecordAsync(
                    new AuditEvent
                    {
                        EntityType = $"Form:{routeName}",
                        Action = AuditAction.FormSubmit,
                        EntityId = null,
                        ChangedValues = changes,
                        User = _userAccessor.GetUserId(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> ValidateSubmission(
        FormMeta meta,
        FormSubmission submission
    )
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in meta.Fields)
        {
            // Required check.
            if (field.Required)
            {
                bool missing;
                if (field.Kind == FieldKind.FileUpload)
                {
                    missing =
                        !submission.Files.TryGetValue(field.Name, out var file) || file is null;
                }
                else
                {
                    submission.Values.TryGetValue(field.Name, out var v);
                    missing = v is null || (v is string s && string.IsNullOrWhiteSpace(s));
                }
                if (missing)
                {
                    errors[field.Name] = $"{field.Label} is required.";
                    continue;
                }
            }

            // Type-specific options.
            if (
                field.Kind == FieldKind.Text
                && field.Options is TextFieldOptions txt
                && txt.MaxLength is int maxLen
            )
            {
                submission.Values.TryGetValue(field.Name, out var v);
                if (v is string s && s.Length > maxLen)
                {
                    errors[field.Name] = $"{field.Label} must be at most {maxLen} characters.";
                    continue;
                }
            }
            else if (field.Kind == FieldKind.Number && field.Options is NumberFieldOptions num)
            {
                submission.Values.TryGetValue(field.Name, out var v);
                if (v is not null && TryToLong(v, out var l))
                {
                    if (num.Min is long min && l < min)
                    {
                        errors[field.Name] = $"{field.Label} must be >= {min}.";
                        continue;
                    }
                    if (num.Max is long max && l > max)
                    {
                        errors[field.Name] = $"{field.Label} must be <= {max}.";
                        continue;
                    }
                }
            }
            else if (field.Kind == FieldKind.Float && field.Options is FloatFieldOptions flt)
            {
                submission.Values.TryGetValue(field.Name, out var v);
                if (v is not null && TryToDouble(v, out var d))
                {
                    if (flt.Min is double min && d < min)
                    {
                        errors[field.Name] = $"{field.Label} must be >= {min}.";
                        continue;
                    }
                    if (flt.Max is double max && d > max)
                    {
                        errors[field.Name] = $"{field.Label} must be <= {max}.";
                        continue;
                    }
                }
            }
            else if (
                field.Kind == FieldKind.FileUpload
                && field.Options is FileUploadFieldOptions fu
            )
            {
                if (submission.Files.TryGetValue(field.Name, out var file) && file is not null)
                {
                    if (fu.MaxSizeBytes is long cap && file.Length > cap)
                    {
                        errors[field.Name] = $"{field.Label} exceeds maximum size of {cap} bytes.";
                        continue;
                    }
                    if (fu.AcceptedExtensions is { Count: > 0 } accepted)
                    {
                        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
                        if (!accepted.Contains(ext))
                        {
                            errors[field.Name] = $"{field.Label} extension '{ext}' is not allowed.";
                            continue;
                        }
                    }
                }
            }

            // User-supplied validators.
            object? candidate = null;
            if (field.Kind == FieldKind.FileUpload)
            {
                submission.Files.TryGetValue(field.Name, out var file);
                candidate = file;
            }
            else
            {
                submission.Values.TryGetValue(field.Name, out candidate);
            }
            foreach (var v in field.Validators)
            {
                var err = v.Validate(candidate);
                if (err is not null)
                {
                    errors[field.Name] = err;
                    break;
                }
            }
        }
        return errors;
    }

    private static bool TryToLong(object value, out long result)
    {
        try
        {
            result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private sealed class NullActionContext : IActionContext
    {
        public Task<bool> ConfirmAsync(string message) => Task.FromResult(true);

        public void ShowSuccess(string message) { }

        public void ShowError(string message) { }

        public void NavigateTo(string url) { }

        public void Refresh() { }
    }

    public async Task<LineChartVM?> LoadLineChartAsync(
        DashboardMeta dashboard,
        string widgetId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        var widget = dashboard
            .Widgets.OfType<LineChartMeta>()
            .FirstOrDefault(w => string.Equals(w.Id, widgetId, StringComparison.Ordinal));
        if (widget is null)
            return null;
        try
        {
            return (LineChartVM)
                await MaterializeWidgetAsync(widget, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (LineChartVM)BuildErrorVM(widget, ex.Message);
        }
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
                return await MaterializeTableWidget(sp, table, cancellationToken)
                    .ConfigureAwait(false);
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

        var columns = (
            meta.VisibleColumns
            ?? entityMeta
                .Columns.Where(c => c.ShowInList && c.Kind != ColumnKind.NavigationCollection)
                .Select(c => c.PropertyName)
                .ToList()
        ).ToList();

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
                    Columns = [],
                    PrimaryKeyPropertyNames = Array.Empty<string>(),
                },
                VisibleColumns = Array.Empty<string>(),
                Rows = Array.Empty<EntityListRowVM>(),
                Error = message,
            },
            _ => throw new InvalidOperationException(
                $"Unknown widget kind '{widget.GetType().Name}'."
            ),
        };

    private async Task EnsureAuthorizedAsync(
        EntityMeta entity,
        AdminAction action,
        object? instance,
        CancellationToken cancellationToken,
        string? actionName = null
    )
    {
        var user = _userAccessor.GetUser();
        var ok = await _authzPolicy
            .IsAuthorizedAsync(entity.Name, action, user, instance, actionName, cancellationToken)
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

        /// <summary>Load the raw entity instance by encoded key, or null when missing.</summary>
        public abstract Task<object?> LoadRawAsync(
            string encodedKey,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Build a fresh entity instance from an edit VM's values, exactly mirroring
        /// what <see cref="CreateAsync"/> does internally before handing the entity
        /// to the data provider. Used by the custom-create handler path so the
        /// handler receives a fully-populated typed instance.
        /// </summary>
        public abstract object MaterializeFromVM(EntityEditVM model);

        /// <summary>
        /// Build the "patched" instance for a custom-update handler: a fresh entity
        /// seeded with every scalar from <paramref name="original"/> (so unchanged
        /// columns survive), then overwritten by every value in <paramref name="model"/>'s
        /// form payload (so the user's edits take effect). The primary key is
        /// preserved from <paramref name="original"/>.
        /// </summary>
        public abstract object MaterializePatched(object original, EntityEditVM model);

        /// <summary>
        /// Project the entity's scalar columns into a name→value dictionary. Used by
        /// the bridge to assemble the <c>ChangedValues</c> snapshot for the audit
        /// event emitted on a successful custom-create.
        /// </summary>
        public abstract IReadOnlyDictionary<string, object?> SnapshotScalarValues(object instance);

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
        private readonly DbContext _dbContext;

        // Null for a type outside the EF model: keys come from the metadata and there are no navigations.
        private readonly Microsoft.EntityFrameworkCore.Metadata.IEntityType? _efEntityType;

        public GenericEntityAdapter(
            EntityMeta meta,
            IServiceProvider sp,
            DbContext dbContext,
            AdminForgeOptions options
        )
        {
            _meta = meta;
            _options = options;
            _dbContext = dbContext;
            _provider = sp.GetRequiredService<IAdminDataProvider<TEntity>>();
            _efEntityType = dbContext.Model.FindEntityType(typeof(TEntity));
            _keyAccessor = _efEntityType is null
                ? new KeyAccessor(typeof(TEntity), meta.PrimaryKeyPropertyNames)
                : new KeyAccessor(_efEntityType);
        }

        public override async Task<object?> LoadRawAsync(
            string encodedKey,
            CancellationToken cancellationToken
        )
        {
            var keyValues = _keyAccessor.DecodeKey(encodedKey);
            return await _provider.FindAsync(keyValues, cancellationToken).ConfigureAwait(false);
        }

        public override async Task<EntityListVM> ListAsync(
            ListQuery query,
            CancellationToken cancellationToken
        )
        {
            // Wire custom columns from the meta into the query — the provider sees them
            // there and (a) lifts sortable/filterable ones into the SQL clause, (b)
            // projects each row's value back through CustomValues.
            var customCols = BuildCustomColumnSpecs();
            var effectiveQuery =
                customCols.Count == 0
                    ? query
                    : new ListQuery
                    {
                        Page = query.Page,
                        PageSize = query.PageSize,
                        SortBy = query.SortBy,
                        SortDescending = query.SortDescending,
                        Filters = query.Filters,
                        Search = query.Search,
                        CustomColumns = customCols,
                    };

            var result = await _provider
                .ListAsync(effectiveQuery, cancellationToken)
                .ConfigureAwait(false);
            var rows = new List<EntityListRowVM>(result.Items.Count);
            for (var i = 0; i < result.Items.Count; i++)
            {
                var item = result.Items[i];
                var rowValues = BuildValueMap(item, includeNavigations: true);
                if (result.CustomValues.Count > i)
                {
                    foreach (var (colName, value) in result.CustomValues[i])
                        rowValues[colName] = value;
                }
                rows.Add(
                    new EntityListRowVM { Key = _keyAccessor.EncodeKey(item), Values = rowValues }
                );
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

        private IReadOnlyDictionary<string, CustomColumnSpec> BuildCustomColumnSpecs()
        {
            if (!_meta.Columns.Any(c => c.IsCustom && c.CustomValueSelector is not null))
                return new Dictionary<string, CustomColumnSpec>();
            var specs = new Dictionary<string, CustomColumnSpec>(StringComparer.Ordinal);
            foreach (var col in _meta.Columns)
            {
                if (!col.IsCustom || col.CustomValueSelector is null)
                    continue;
                specs[col.PropertyName] = new CustomColumnSpec(
                    col.CustomValueSelector,
                    col.IsSortable,
                    col.IsFilterable
                );
            }
            return specs;
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
            var relatedLinks = await BuildRelatedLinksAsync(entity, cancellationToken)
                .ConfigureAwait(false);
            return new EntityViewVM
            {
                EntityName = _meta.Name,
                Key = _keyAccessor.EncodeKey(entity),
                Values = values,
                RelatedLinks = relatedLinks,
            };
        }

        /// <summary>
        /// Materialise related-link descriptors for the entity view page: one per
        /// collection navigation (unless suppressed via <c>HideRelatedLink</c>), plus
        /// any cross-entity <see cref="RelatedLinkMeta"/> registered explicitly.
        /// </summary>
        private async Task<IReadOnlyList<RelatedLinkVM>> BuildRelatedLinksAsync(
            TEntity sourceInstance,
            CancellationToken cancellationToken
        )
        {
            var explicitBySourceNav = new Dictionary<string, RelatedLinkMeta>(
                StringComparer.Ordinal
            );
            var crossEntityLinks = new List<RelatedLinkMeta>();
            foreach (var link in _meta.RelatedLinks)
            {
                if (link.SourceNavigationName is { } navName)
                    explicitBySourceNav[navName] = link;
                else
                    crossEntityLinks.Add(link);
            }

            var output = new List<RelatedLinkVM>();

            // Auto + override links from collection navigations.
            var navigations = _efEntityType is null
                ? []
                : _efEntityType
                    .GetNavigations()
                    .Concat<Microsoft.EntityFrameworkCore.Metadata.INavigationBase>(
                        _efEntityType.GetSkipNavigations()
                    );
            foreach (var nav in navigations)
            {
                if (!nav.IsCollection)
                    continue;
                if (_meta.HiddenRelatedNavigations.Contains(nav.Name))
                    continue;

                // Find the target meta — only links to registered entities are auto-emitted.
                var targetType = nav.TargetEntityType.ClrType;
                var targetMeta = _options.Entities.FirstOrDefault(e => e.ClrType == targetType);
                if (targetMeta is null)
                    continue;

                // Inverse FK: for a regular collection nav (Tag.Todos), the FK lives on
                // the other side (Todo.AssigneeId / Todo.TagId etc.). For an implicit
                // M2M skip-nav we can't pre-filter (no scalar FK in the model), so we
                // skip auto-emission — explicit RelatedLink<TTarget> covers that case.
                IReadOnlyDictionary<string, object?> filter;
                if (nav is Microsoft.EntityFrameworkCore.Metadata.INavigation regular)
                {
                    var fk = regular.ForeignKey;
                    if (fk.Properties.Count != 1 || fk.PrincipalKey.Properties.Count != 1)
                        continue;
                    var fkPropName = fk.Properties[0].Name;
                    var pkPropName = fk.PrincipalKey.Properties[0].Name;
                    var pkValue = typeof(TEntity)
                        .GetProperty(pkPropName, BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(sourceInstance);
                    filter = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [fkPropName] = pkValue,
                    };
                }
                else
                {
                    continue;
                }

                // Build label: explicit override wins; otherwise "View N {label}".
                string label;
                string? icon = null;
                if (explicitBySourceNav.TryGetValue(nav.Name, out var explicitMeta))
                {
                    label = explicitMeta.Label;
                    icon = explicitMeta.Icon;
                }
                else
                {
                    var count = await CountRelatedAsync(nav.Name, filter, cancellationToken)
                        .ConfigureAwait(false);
                    label = $"View {count} {targetMeta.Label}";
                }

                output.Add(
                    new RelatedLinkVM
                    {
                        Label = label,
                        Icon = icon,
                        RouteName = targetMeta.RouteName,
                        Filter = filter,
                    }
                );
            }

            // Cross-entity explicit links (no source nav): use the user-supplied filter builder verbatim.
            foreach (var link in crossEntityLinks)
            {
                var targetMeta = _options.Entities.FirstOrDefault(e =>
                    e.ClrType == link.RelatedEntityType
                );
                if (targetMeta is null)
                    continue;
                var filter = link.FilterBuilder(sourceInstance);
                output.Add(
                    new RelatedLinkVM
                    {
                        Label = link.Label,
                        Icon = link.Icon,
                        RouteName = targetMeta.RouteName,
                        Filter = filter,
                    }
                );
            }

            return output;
        }

        /// <summary>
        /// Cheap COUNT query against the related entity restricted by <paramref name="filter"/>.
        /// Used only for the "View N {label}" auto-label — eats one extra round-trip per
        /// collection nav, which we accept given typical admin pages have a handful of these.
        /// </summary>
        private async Task<int> CountRelatedAsync(
            string sourceNavName,
            IReadOnlyDictionary<string, object?> filter,
            CancellationToken cancellationToken
        )
        {
            var nav = _efEntityType?.FindNavigation(sourceNavName);
            if (nav is null)
                return 0;
            var targetClrType = nav.TargetEntityType.ClrType;
            var setMethod = typeof(DbContext)
                .GetMethods()
                .First(m =>
                    m.Name == nameof(DbContext.Set)
                    && m.IsGenericMethod
                    && m.GetParameters().Length == 0
                )
                .MakeGenericMethod(targetClrType);
            var dbSet = setMethod.Invoke(_dbContext, null);
            if (dbSet is not IQueryable queryable)
                return 0;

            // Apply equality filters via a built lambda. Reuse provider's static helpers where possible.
            var entityParam = Expression.Parameter(targetClrType, "e");
            Expression? body = null;
            foreach (var (propName, val) in filter)
            {
                var prop = targetClrType.GetProperty(
                    propName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null)
                    continue;
                var member = Expression.Property(entityParam, prop);
                var coerced = val is null
                    ? null
                    : Convert.ChangeType(
                        val,
                        Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType,
                        CultureInfo.InvariantCulture
                    );
                Expression constant =
                    coerced is null
                    && prop.PropertyType.IsValueType
                    && Nullable.GetUnderlyingType(prop.PropertyType) is null
                        ? Expression.Default(prop.PropertyType)
                        : Expression.Constant(coerced, prop.PropertyType);
                var eq = Expression.Equal(member, constant);
                body = body is null ? eq : Expression.AndAlso(body, eq);
            }
            if (body is null)
                return await CountQueryableAsync(queryable, cancellationToken)
                    .ConfigureAwait(false);

            var lambda = Expression.Lambda(body, entityParam);
            var whereMethod = typeof(Queryable)
                .GetMethods()
                .First(m =>
                    m.Name == nameof(Queryable.Where)
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[1]
                        .ParameterType.GetGenericArguments()[0]
                        .GetGenericArguments()
                        .Length == 2
                )
                .MakeGenericMethod(targetClrType);
            var filtered = (IQueryable)whereMethod.Invoke(null, [queryable, lambda])!;
            return await CountQueryableAsync(filtered, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<int> CountQueryableAsync(
            IQueryable queryable,
            CancellationToken ct
        )
        {
            // Use EF's async CountAsync via reflection so we don't bind the open-generic call here.
            var elementType = queryable.ElementType;
            var asyncCount = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .First(m =>
                    m.Name == nameof(EntityFrameworkQueryableExtensions.CountAsync)
                    && m.GetParameters().Length == 2
                )
                .MakeGenericMethod(elementType);
            var task = (Task<int>)asyncCount.Invoke(null, [queryable, ct])!;
            return await task.ConfigureAwait(false);
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
            var entity = (TEntity)MaterializeFromVM(model);
            var created = await _provider
                .CreateAsync(entity, cancellationToken)
                .ConfigureAwait(false);
            return _keyAccessor.EncodeKey(created);
        }

        public override object MaterializeFromVM(EntityEditVM model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var entity = Activator.CreateInstance<TEntity>();
            ApplyValues(entity, model.Values, includePk: true);
            return entity;
        }

        public override object MaterializePatched(object original, EntityEditVM model)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(model);
            var typedOriginal = (TEntity)original;
            var patched = Activator.CreateInstance<TEntity>();

            // Seed every writable scalar property from the original so columns that
            // weren't in the form (HiddenInEdit, generated, etc.) survive the round-trip.
            foreach (var column in _meta.Columns)
            {
                if (
                    column.Kind == ColumnKind.NavigationReference
                    || column.Kind == ColumnKind.NavigationCollection
                    || column.Kind == ColumnKind.Owned
                )
                    continue;
                if (column.IsCustom)
                    continue;
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanRead || !prop.CanWrite)
                    continue;
                prop.SetValue(patched, prop.GetValue(typedOriginal));
            }

            // Overlay the form-supplied values. Skip PK (preserved from original above).
            ApplyValues(patched, model.Values, includePk: false);
            return patched;
        }

        public override IReadOnlyDictionary<string, object?> SnapshotScalarValues(object instance)
        {
            ArgumentNullException.ThrowIfNull(instance);
            var typed = (TEntity)instance;
            var snap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var column in _meta.Columns)
            {
                if (
                    column.Kind == ColumnKind.NavigationReference
                    || column.Kind == ColumnKind.NavigationCollection
                    || column.Kind == ColumnKind.Owned
                )
                    continue;
                if (column.IsCustom)
                    continue; // computed columns aren't part of the persisted state
                var prop = typeof(TEntity).GetProperty(
                    column.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (prop is null || !prop.CanRead)
                    continue;
                snap[column.PropertyName] = prop.GetValue(typed);
            }
            return snap;
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
                    values[column.PropertyName] = BuildNavRef(
                        raw,
                        column.RelatedEntityType,
                        column.LinkTextResolver
                    );
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
                        var navRef = BuildNavRef(
                            item,
                            column.RelatedEntityType,
                            linkTextResolver: null
                        );
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

        private NavigationRef? BuildNavRef(
            object relatedInstance,
            Type? relatedType,
            Func<object, string>? linkTextResolver
        )
        {
            if (relatedType is null)
                return null;
            var meta = _options.Entities.FirstOrDefault(e => e.ClrType == relatedType);
            if (meta is null)
                return null;
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
            // Source-side LinkText override beats the related entity's DisplayLabel.
            var label =
                linkTextResolver?.Invoke(relatedInstance)
                ?? meta.DisplayLabel?.Invoke(relatedInstance)
                ?? encodedKey;
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
