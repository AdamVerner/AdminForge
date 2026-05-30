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

    public EfCoreDataProvider(
        TContext context,
        IAuditSink? auditSink,
        IUserAccessor? userAccessor
    )
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

        IQueryable<TEntity> queryable = _context.Set<TEntity>().AsNoTracking();
        queryable = ApplyFilters(queryable, query.Filters);
        queryable = ApplySearch(queryable, query.Search);

        var total = await queryable.CountAsync(cancellationToken).ConfigureAwait(false);

        queryable = ApplySort(queryable, query.SortBy, query.SortDescending);
        // Fall back to a stable default sort (first PK property ascending) so the
        // Skip/Take pagination is deterministic across providers.
        if (string.IsNullOrWhiteSpace(query.SortBy))
        {
            var pkName = _keyAccessor.KeyProperties.Count > 0
                ? _keyAccessor.KeyProperties[0].Name
                : null;
            if (pkName is not null)
            {
                queryable = ApplySort(queryable, pkName, descending: false);
            }
        }
        queryable = queryable.Skip(query.Page * query.PageSize).Take(query.PageSize);

        var items = await queryable.ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ListResult<TEntity> { Items = items, TotalCount = total };
    }

    public async Task<TEntity?> FindAsync(
        object?[] keyValues,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        // DbSet.FindAsync handles composite keys via the EF model's PK metadata.
        return await _context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);
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
            await _auditSink.RecordAsync(
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
            ).ConfigureAwait(false);
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
            await _auditSink.RecordAsync(
                new AuditEvent
                {
                    EntityType = _entityName,
                    Action = AuditAction.Update,
                    EntityId = _keyAccessor.EncodeKeyValues(keyValues),
                    ChangedValues = changes,
                    User = _userAccessor?.GetUserId(),
                },
                cancellationToken
            ).ConfigureAwait(false);
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
            await _auditSink.RecordAsync(
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
            ).ConfigureAwait(false);
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

    private static readonly MethodInfo _efLike = typeof(Microsoft.EntityFrameworkCore.DbFunctionsExtensions)
        .GetMethod(
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
