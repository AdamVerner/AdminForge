using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using AdminForge.Core.Contracts;
using AdminForge.Core.Metadata;
using Microsoft.EntityFrameworkCore;

namespace AdminForge.DataAccess.EfCore;

/// <summary>
/// Default <see cref="IAdminDataProvider{T}"/> backed by an EF Core
/// <see cref="DbContext"/>. Filtering, sorting, and paging compose into a
/// single <see cref="IQueryable{T}"/> chain so providers like SQLite and
/// SQL Server can translate them server-side.
/// </summary>
public class EfCoreDataProvider<TContext, TEntity> : IAdminDataProvider<TEntity>
    where TContext : DbContext
    where TEntity : class
{
    private readonly TContext _context;
    private readonly KeyAccessor _keyAccessor;
    private readonly IAuditSink? _auditSink;
    private readonly IUserAccessor? _userAccessor;
    private readonly string _entityName;
    private readonly IReadOnlyList<string> _scalarPropertyNames;

    public EfCoreDataProvider(TContext context)
        : this(context, auditSink: null, userAccessor: null) { }

    public EfCoreDataProvider(TContext context, IAuditSink? auditSink, IUserAccessor? userAccessor)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var entityType =
            context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).FullName}' is not part of the context model."
            );
        _keyAccessor = new KeyAccessor(entityType);
        _auditSink = auditSink;
        _userAccessor = userAccessor;
        _entityName = typeof(TEntity).Name;
        _scalarPropertyNames = entityType
            .GetProperties()
            .Where(p => !p.IsShadowProperty())
            .Select(p => p.Name)
            .ToArray();
    }

    public async Task<ListResult<TEntity>> ListAsync(
        ListQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.PageSize <= 0)
            throw new ArgumentException("PageSize must be positive.", nameof(query));
        if (query.Page < 0)
            throw new ArgumentException("Page must be non-negative.", nameof(query));

        // Split native-property filters from custom-column filters before lowering.
        var nativeFilters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var customFilters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, val) in query.Filters)
        {
            if (query.CustomColumns.TryGetValue(key, out var spec) && spec.Filterable)
                customFilters[key] = val;
            else
                nativeFilters[key] = val;
        }

        IQueryable<TEntity> queryable = _context.Set<TEntity>().AsNoTracking();
        queryable = ApplyFilters(queryable, nativeFilters);
        queryable = ApplyCustomFilters(queryable, customFilters, query.CustomColumns);
        queryable = ApplySearch(queryable, query.Search);

        var total = await queryable.CountAsync(cancellationToken).ConfigureAwait(false);

        // Sort: if SortBy targets a sortable custom column, project via the user's
        // selector; otherwise fall back to property-based sorting.
        var isCustomSort =
            !string.IsNullOrWhiteSpace(query.SortBy)
            && query.CustomColumns.TryGetValue(query.SortBy!, out var sortSpec)
            && sortSpec.Sortable;
        if (isCustomSort)
        {
            queryable = ApplyCustomSort(
                queryable,
                query.CustomColumns[query.SortBy!].Selector,
                query.SortDescending
            );
        }
        else
        {
            queryable = ApplySort(queryable, query.SortBy, query.SortDescending);
        }

        // Fall back to a stable default sort (first PK property ascending) so the
        // Skip/Take pagination is deterministic across providers.
        if (!isCustomSort && string.IsNullOrWhiteSpace(query.SortBy))
        {
            var pkName =
                _keyAccessor.KeyProperties.Count > 0 ? _keyAccessor.KeyProperties[0].Name : null;
            if (pkName is not null)
            {
                queryable = ApplySort(queryable, pkName, descending: false);
            }
        }
        queryable = queryable.Skip(query.Page * query.PageSize).Take(query.PageSize);

        var items = await queryable.ToListAsync(cancellationToken).ConfigureAwait(false);

        // Computed-column projection: pragmatic side-query approach. For each registered
        // custom column we issue a single query that pairs the entity's encoded primary
        // key with the projected value across the materialised page rows. This keeps
        // the SQL trivial (no row-by-row N+1) at the cost of one extra query per custom
        // column — acceptable for admin pages (small pages, few custom columns) and
        // sidesteps the complexity of dynamically-built tuple selects.
        IReadOnlyList<IReadOnlyDictionary<string, object?>> customValues = Array.Empty<
            IReadOnlyDictionary<string, object?>
        >();
        if (query.CustomColumns.Count > 0 && items.Count > 0)
        {
            customValues = await ProjectCustomColumns(items, query.CustomColumns, cancellationToken)
                .ConfigureAwait(false);
        }

        return new ListResult<TEntity>
        {
            Items = items,
            TotalCount = total,
            CustomValues = customValues,
        };
    }

    /// <summary>
    /// Compute every custom column for the current page in a single server-side query.
    /// <para>
    /// Builds a composite projection of the form
    /// <c>q.Where(e =&gt; pks.Contains(e.Pk)).Select(e =&gt; new ProjectionRow {
    /// Key = e.Pk, V0 = selectorA(e), V1 = selectorB(e), ... })</c>. The row type is
    /// constructed at runtime (one closed generic per (PK type × custom-column count)
    /// shape, cached) so every custom column collapses into one round-trip regardless
    /// of count. Falls back to per-row in-process evaluation for composite keys.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ProjectCustomColumns(
        IReadOnlyList<TEntity> pageItems,
        IReadOnlyDictionary<string, CustomColumnSpec> customColumns,
        CancellationToken cancellationToken
    )
    {
        var rowDicts = new Dictionary<string, object?>[pageItems.Count];
        for (var i = 0; i < pageItems.Count; i++)
            rowDicts[i] = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Preserve a stable column order so the projection's positional slots line up.
        var orderedColumns = customColumns.Select(kvp => (Name: kvp.Key, Spec: kvp.Value)).ToList();
        if (orderedColumns.Count == 0)
            return rowDicts;

        var pkProperties = _keyAccessor.KeyProperties;

        // Composite-key fallback: evaluate selectors in-process per row. Less ideal for
        // selectors that touch the DB (e.g. nav.Count) — but admin pages with composite
        // keys are rare and we lose pushdown only in that edge case.
        if (pkProperties.Count != 1 || pkProperties[0].PropertyInfo is null)
        {
            for (var i = 0; i < orderedColumns.Count; i++)
            {
                var compiled = orderedColumns[i].Spec.Selector.Compile();
                for (var r = 0; r < pageItems.Count; r++)
                {
                    object? value;
                    try
                    {
                        value = compiled.DynamicInvoke(pageItems[r]);
                    }
                    catch
                    {
                        value = null;
                    }
                    rowDicts[r][orderedColumns[i].Name] = value;
                }
            }
            return rowDicts;
        }

        var pkInfo = pkProperties[0].PropertyInfo!;

        // Materialise PKs for the page (Contains(...) input).
        var typedKeysArray = Array.CreateInstance(pkInfo.PropertyType, pageItems.Count);
        for (var i = 0; i < pageItems.Count; i++)
            typedKeysArray.SetValue(pkInfo.GetValue(pageItems[i]), i);

        // Build the projection row type for this (PK type, N) shape (cached).
        var rowType = GetProjectionRowType(pkInfo.PropertyType, orderedColumns.Count);

        // Compose: e => new ProjectionRow<PK,N> { Key = e.Pk, V0 = sel0(e), V1 = sel1(e), ... }
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var bindings = new List<MemberBinding>(orderedColumns.Count + 1)
        {
            Expression.Bind(rowType.GetProperty("Key")!, Expression.Property(entityParam, pkInfo)),
        };
        for (var i = 0; i < orderedColumns.Count; i++)
        {
            var selector = orderedColumns[i].Spec.Selector;
            var replacer = new ParameterReplacer(selector.Parameters[0], entityParam);
            var body = replacer.Visit(selector.Body)!;
            bindings.Add(
                Expression.Bind(
                    rowType.GetProperty($"V{i}")!,
                    Expression.Convert(body, typeof(object))
                )
            );
        }
        var memberInit = Expression.MemberInit(Expression.New(rowType), bindings);
        var selectorLambda = Expression.Lambda(
            typeof(Func<,>).MakeGenericType(typeof(TEntity), rowType),
            memberInit,
            entityParam
        );

        // Where(e => keys.Contains(e.Pk))
        var pkAccess = Expression.Property(entityParam, pkInfo);
        var containsMethod = typeof(System.Linq.Enumerable)
            .GetMethods()
            .First(m =>
                m.Name == nameof(System.Linq.Enumerable.Contains) && m.GetParameters().Length == 2
            )
            .MakeGenericMethod(pkInfo.PropertyType);
        var keysConstant = Expression.Constant(typedKeysArray, typedKeysArray.GetType());
        var containsCall = Expression.Call(null, containsMethod, keysConstant, pkAccess);
        var whereLambda = Expression.Lambda<Func<TEntity, bool>>(containsCall, entityParam);

        var dbSet = _context.Set<TEntity>().AsNoTracking();
        var filtered = dbSet.Where(whereLambda);

        var selectMethod = typeof(Queryable)
            .GetMethods()
            .First(m =>
                m.Name == nameof(Queryable.Select)
                && m.GetParameters().Length == 2
                && m.GetParameters()[1]
                    .ParameterType.GetGenericArguments()[0]
                    .GetGenericArguments()
                    .Length == 2
            )
            .MakeGenericMethod(typeof(TEntity), rowType);

        var projectedQuery = (IQueryable)selectMethod.Invoke(null, [filtered, selectorLambda])!;

        // Cancellation: EF translates this query asynchronously; iterate via the async
        // enumerable path so the token honours.
        var rows = new List<object>();
        await foreach (
            var item in projectedQuery
                .Cast<object>()
                .AsAsyncEnumerable()
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false)
        )
            rows.Add(item);

        // Index by PK then write into the per-row dictionaries in the original page order.
        var keyProp = rowType.GetProperty("Key")!;
        var valueProps = new PropertyInfo[orderedColumns.Count];
        for (var i = 0; i < orderedColumns.Count; i++)
            valueProps[i] = rowType.GetProperty($"V{i}")!;

        var byKey = new Dictionary<object, object>(rows.Count);
        foreach (var row in rows)
        {
            var k = keyProp.GetValue(row);
            if (k is not null)
                byKey[k] = row;
        }
        for (var r = 0; r < pageItems.Count; r++)
        {
            var k = pkInfo.GetValue(pageItems[r]);
            if (k is null || !byKey.TryGetValue(k, out var row))
            {
                for (var i = 0; i < orderedColumns.Count; i++)
                    rowDicts[r][orderedColumns[i].Name] = null;
                continue;
            }
            for (var i = 0; i < orderedColumns.Count; i++)
                rowDicts[r][orderedColumns[i].Name] = valueProps[i].GetValue(row);
        }
        return rowDicts;
    }

    // Cache of closed generic projection row types keyed by (PK CLR type, custom-column count).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (Type, int),
        Type
    > _projectionRowTypes = new();

    private static Type GetProjectionRowType(Type pkType, int valueCount) =>
        _projectionRowTypes.GetOrAdd(
            (pkType, valueCount),
            static key =>
            {
                // The arity of ProjectionRow varies; rather than emit a TypeBuilder type per
                // shape, we use a small family of pre-declared generics. We support up to
                // 8 custom columns per page query (deep beyond that we wouldn't expect a
                // human admin page to go); throw a clear error otherwise so a follow-up
                // can extend the family rather than silently degrading.
                var (pk, n) = key;
                var openType = n switch
                {
                    0 => typeof(CustomProjectionRow<>),
                    1 => typeof(CustomProjectionRow<,>),
                    2 => typeof(CustomProjectionRow<,,>),
                    3 => typeof(CustomProjectionRow<,,,>),
                    4 => typeof(CustomProjectionRow<,,,,>),
                    5 => typeof(CustomProjectionRow<,,,,,>),
                    6 => typeof(CustomProjectionRow<,,,,,,>),
                    7 => typeof(CustomProjectionRow<,,,,,,,>),
                    8 => typeof(CustomProjectionRow<,,,,,,,,>),
                    _ => throw new InvalidOperationException(
                        $"Custom-column projection supports up to 8 columns per page; got {n}."
                    ),
                };
                // All value slots are object so EF can box arbitrary expression types into them.
                var typeArgs = new Type[n + 1];
                typeArgs[0] = pk;
                for (var i = 1; i <= n; i++)
                    typeArgs[i] = typeof(object);
                return openType.MakeGenericType(typeArgs);
            }
        );

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to)
        : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }

    public async Task<TEntity?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        // FindAsync alone doesn't eager-load navigation properties, so view pages
        // (and `LinkText` resolvers that key off the navigation target) would see
        // null references. Build a Where-by-key query and Include each reference
        // navigation. We skip collection navs — the related-link section pulls
        // those via the bridge's own COUNT query.
        var entityType = _context.Model.FindEntityType(typeof(TEntity));
        if (entityType is null)
            return await _context
                .Set<TEntity>()
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);

        var keyProperties = _keyAccessor.KeyProperties;
        if (keyProperties.Count == 0 || keyValues.Length != keyProperties.Count)
            return await _context
                .Set<TEntity>()
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);

        // Build a Where clause: e => e.K1 == k1 && e.K2 == k2 ...
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression? predicate = null;
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var info = keyProperties[i].PropertyInfo;
            if (info is null)
                return await _context
                    .Set<TEntity>()
                    .FindAsync(keyValues, cancellationToken)
                    .ConfigureAwait(false);
            var member = Expression.Property(parameter, info);
            var coerced = keyValues[i];
            Expression constant =
                coerced is null
                && info.PropertyType.IsValueType
                && Nullable.GetUnderlyingType(info.PropertyType) is null
                    ? Expression.Default(info.PropertyType)
                    : Expression.Constant(coerced, info.PropertyType);
            var eq = Expression.Equal(member, constant);
            predicate = predicate is null ? eq : Expression.AndAlso(predicate, eq);
        }
        if (predicate is null)
            return await _context
                .Set<TEntity>()
                .FindAsync(keyValues, cancellationToken)
                .ConfigureAwait(false);

        IQueryable<TEntity> queryable = _context.Set<TEntity>().AsNoTracking();
        foreach (var nav in entityType.GetNavigations())
        {
            if (nav.IsCollection)
                continue; // skip collections (potentially expensive)
            queryable = queryable.Include(nav.Name);
        }
        var lambda = Expression.Lambda<Func<TEntity, bool>>(predicate, parameter);
        return await queryable.FirstOrDefaultAsync(lambda, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TEntity> CreateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _context.Set<TEntity>().AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_auditSink is not null)
        {
            var snapshot = SnapshotScalarValues(entity);
            await _auditSink
                .RecordAsync(
                    new AuditEvent
                    {
                        EntityType = _entityName,
                        Action = AuditAction.Create,
                        EntityId = _keyAccessor.EncodeKey(entity),
                        ChangedValues = snapshot.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new AuditValueChange(null, kvp.Value)
                        ),
                        User = _userAccessor?.GetUserId(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return entity;
    }

    public async Task<TEntity> UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(entity);
        var keyValues = _keyAccessor.GetKeyValues(entity);
        var tracked = await _context
            .Set<TEntity>()
            .FindAsync(keyValues, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
            throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' with key '{_keyAccessor.EncodeKeyValues(keyValues)}' not found."
            );

        // Capture before-values from EF's snapshot of the original database row, NOT from
        // the tracked entity reference — callers commonly mutate the returned instance
        // in place (e.g. after a previous CreateAsync) so `tracked` may already carry the
        // new values when we get here.
        IReadOnlyDictionary<string, object?>? before = null;
        if (_auditSink is not null)
        {
            var originalValues = _context.Entry(tracked).OriginalValues;
            var snap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var prop in originalValues.Properties)
            {
                snap[prop.Name] = originalValues[prop.Name];
            }
            before = snap;
        }

        _context.Entry(tracked).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_auditSink is not null && before is not null)
        {
            var after = SnapshotScalarValues(tracked);
            var changes = new Dictionary<string, AuditValueChange>();
            foreach (var (key, newVal) in after)
            {
                before.TryGetValue(key, out var oldVal);
                if (!Equals(oldVal, newVal))
                {
                    changes[key] = new AuditValueChange(oldVal, newVal);
                }
            }
            await _auditSink
                .RecordAsync(
                    new AuditEvent
                    {
                        EntityType = _entityName,
                        Action = AuditAction.Update,
                        EntityId = _keyAccessor.EncodeKeyValues(keyValues),
                        ChangedValues = changes,
                        User = _userAccessor?.GetUserId(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return tracked;
    }

    public async Task<bool> DeleteAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        var entity = await _context
            .Set<TEntity>()
            .FindAsync(keyValues, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;

        IReadOnlyDictionary<string, object?>? before = _auditSink is null
            ? null
            : SnapshotScalarValues(entity);

        _context.Set<TEntity>().Remove(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_auditSink is not null && before is not null)
        {
            await _auditSink
                .RecordAsync(
                    new AuditEvent
                    {
                        EntityType = _entityName,
                        Action = AuditAction.Delete,
                        EntityId = _keyAccessor.EncodeKeyValues(keyValues),
                        ChangedValues = before.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new AuditValueChange(kvp.Value, null)
                        ),
                        User = _userAccessor?.GetUserId(),
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return true;
    }

    private Dictionary<string, object?> SnapshotScalarValues(TEntity entity)
    {
        var snap = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in _scalarPropertyNames)
        {
            var property = typeof(TEntity).GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance
            );
            if (property is null || !property.CanRead)
                continue;
            snap[name] = property.GetValue(entity);
        }
        return snap;
    }

    private static IQueryable<TEntity> ApplyCustomFilters(
        IQueryable<TEntity> source,
        IReadOnlyDictionary<string, object?> customFilters,
        IReadOnlyDictionary<string, CustomColumnSpec> customColumns
    )
    {
        if (customFilters.Count == 0)
            return source;

        foreach (var (name, rawValue) in customFilters)
        {
            if (!customColumns.TryGetValue(name, out var spec))
                continue;

            // Build Where(e => spec.Selector(e).Equals(coercedValue)) by inlining
            // the user's selector into a fresh predicate.
            var entityParam = Expression.Parameter(typeof(TEntity), "e");
            var replacer = new ParameterReplacer(spec.Selector.Parameters[0], entityParam);
            var selectorBody = replacer.Visit(spec.Selector.Body)!;

            var selectorReturnType = spec.Selector.ReturnType;
            var coerced = CoerceToType(rawValue, selectorReturnType);
            // Expression.Constant(null, valueType) throws on non-nullable value types;
            // wrap in default(T) if coerced is null and the type can't hold null.
            Expression valueExpr;
            if (
                coerced is null
                && selectorReturnType.IsValueType
                && Nullable.GetUnderlyingType(selectorReturnType) is null
            )
            {
                valueExpr = Expression.Default(selectorReturnType);
            }
            else
            {
                valueExpr = Expression.Constant(coerced, selectorReturnType);
            }

            Expression equals = Expression.Equal(selectorBody, valueExpr);
            var lambda = Expression.Lambda<Func<TEntity, bool>>(equals, entityParam);
            source = source.Where(lambda);
        }
        return source;
    }

    private static IQueryable<TEntity> ApplyCustomSort(
        IQueryable<TEntity> source,
        LambdaExpression selector,
        bool descending
    )
    {
        var entityParam = Expression.Parameter(typeof(TEntity), "e");
        var replacer = new ParameterReplacer(selector.Parameters[0], entityParam);
        var body = replacer.Visit(selector.Body)!;
        var keySelector = Expression.Lambda(body, entityParam);

        var methodName = descending ? "OrderByDescending" : "OrderBy";
        var orderCall = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(TEntity), selector.ReturnType],
            source.Expression,
            Expression.Quote(keySelector)
        );
        return source.Provider.CreateQuery<TEntity>(orderCall);
    }

    private static IQueryable<TEntity> ApplyFilters(
        IQueryable<TEntity> source,
        IReadOnlyDictionary<string, object?> filters
    )
    {
        if (filters.Count == 0)
            return source;

        foreach (var (propertyName, rawValue) in filters)
        {
            var property = ResolveProperty(propertyName);
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var memberAccess = Expression.Property(parameter, property);

            var typedValue = CoerceToType(rawValue, property.PropertyType);
            var constant = Expression.Constant(typedValue, property.PropertyType);
            var predicate = Expression.Lambda<Func<TEntity, bool>>(
                Expression.Equal(memberAccess, constant),
                parameter
            );
            source = source.Where(predicate);
        }
        return source;
    }

    private static readonly MethodInfo _efLike =
        typeof(Microsoft.EntityFrameworkCore.DbFunctionsExtensions).GetMethod(
            nameof(Microsoft.EntityFrameworkCore.DbFunctionsExtensions.Like),
            [typeof(Microsoft.EntityFrameworkCore.DbFunctions), typeof(string), typeof(string)]
        )!;

    private static IQueryable<TEntity> ApplySearch(IQueryable<TEntity> source, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return source;

        // Build EF.Functions.Like(e.PropA, "%search%") OR Like(e.PropB, ...) ...
        // LIKE is the case-insensitive default on SQLite and on SQL Server's
        // default collation, and translates cleanly without requiring a
        // ".ToLower()" rewrite on either side.
        var stringProps = typeof(TEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .ToArray();
        if (stringProps.Length == 0)
            return source;

        var escaped = search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        var pattern = $"%{escaped}%";

        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var functions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
        var patternConstant = Expression.Constant(pattern, typeof(string));
        Expression? body = null;
        foreach (var prop in stringProps)
        {
            var member = Expression.Property(parameter, prop);
            var like = Expression.Call(null, _efLike, functions, member, patternConstant);
            body = body is null ? like : Expression.OrElse(body, like);
        }
        if (body is null)
            return source;
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, parameter);
        return source.Where(lambda);
    }

    private static IQueryable<TEntity> ApplySort(
        IQueryable<TEntity> source,
        string? sortBy,
        bool descending
    )
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return source;

        var property = ResolveProperty(sortBy);
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var memberAccess = Expression.Property(parameter, property);
        var keySelector = Expression.Lambda(memberAccess, parameter);

        var methodName = descending ? "OrderByDescending" : "OrderBy";
        var orderCall = Expression.Call(
            typeof(Queryable),
            methodName,
            [typeof(TEntity), property.PropertyType],
            source.Expression,
            Expression.Quote(keySelector)
        );
        return source.Provider.CreateQuery<TEntity>(orderCall);
    }

    private static PropertyInfo ResolveProperty(string propertyName)
    {
        var property =
            typeof(TEntity).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            )
            ?? throw new ArgumentException(
                $"Entity '{typeof(TEntity).Name}' has no public property '{propertyName}'.",
                nameof(propertyName)
            );
        return property;
    }

    private static object? CoerceToType(object? value, Type targetType)
    {
        if (value is null)
            return null;
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying.IsInstanceOfType(value))
            return value;
        if (underlying.IsEnum && value is string s)
            return Enum.Parse(underlying, s, ignoreCase: true);
        if (value is string str)
        {
            if (underlying == typeof(Guid))
                return Guid.Parse(str);
            return Convert.ChangeType(str, underlying, CultureInfo.InvariantCulture);
        }
        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }
}

// Projection row types used by EfCoreDataProvider.ProjectCustomColumns. Declared at
// the namespace level so MakeGenericType() doesn't have to close over the enclosing
// EfCoreDataProvider<TContext,TEntity> generic context. Each shape carries the
// entity's PK as Key and the custom-column projections as V0..V{N-1}. Value slots
// are typed as object so any EF-translatable expression result fits.
public sealed class CustomProjectionRow<TKey>
{
    public TKey? Key { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2, TV3>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2, TV3, TV4>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
    public object? V4 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2, TV3, TV4, TV5>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
    public object? V4 { get; set; }
    public object? V5 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2, TV3, TV4, TV5, TV6>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
    public object? V4 { get; set; }
    public object? V5 { get; set; }
    public object? V6 { get; set; }
}

public sealed class CustomProjectionRow<TKey, TV0, TV1, TV2, TV3, TV4, TV5, TV6, TV7>
{
    public TKey? Key { get; set; }
    public object? V0 { get; set; }
    public object? V1 { get; set; }
    public object? V2 { get; set; }
    public object? V3 { get; set; }
    public object? V4 { get; set; }
    public object? V5 { get; set; }
    public object? V6 { get; set; }
    public object? V7 { get; set; }
}
