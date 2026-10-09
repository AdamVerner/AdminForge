using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AdminForge;

/// <summary>
/// Serves one <c>DbSet</c>: search, filters and sort in SQL, find by key, create, update of the
/// changed properties only, delete. Subclass it and override a method to add a rule.
/// </summary>
public class EfSource<TDb, T>(TDb db)
    : IAdminSource<T, object?[]>,
        IAdminWritable<T>,
        IAdminDeletable<T>,
        IQueriesAnything
    where TDb : DbContext
    where T : class
{
    protected TDb Db { get; } = db;

    public virtual async Task<AdminPage<T>> ListAsync(AdminQuery<T> query, CancellationToken ct)
    {
        IQueryable<T> rows = Db.Set<T>().AsNoTracking();
        var row = Expression.Parameter(typeof(T), "row");
        foreach (var (name, value) in query.Filters)
        {
            var member = Expression.Property(row, name);
            Expression test =
                member.Type == typeof(string) && value is string text && !query.Exact.Contains(name)
                    ? Like(member, text)
                    : Expression.Equal(member, Expression.Constant(value, member.Type));
            rows = rows.Where(Expression.Lambda<Func<T, bool>>(test, row));
        }
        if (query.Search is { } search)
        {
            var any = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.PropertyType == typeof(string)
                    && Db.Model.FindEntityType(typeof(T))?.FindProperty(p.Name) is not null
                )
                .Select(p => Like(Expression.Property(row, p), search))
                .Aggregate(
                    (Expression?)null,
                    (all, one) => all is null ? one : Expression.OrElse(all, one)
                );
            if (any is not null)
                rows = rows.Where(Expression.Lambda<Func<T, bool>>(any, row));
        }
        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        // A stable order, so paging is deterministic: the requested column, else the key.
        var sort =
            query.SortBy
            ?? Db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties[0].Name;
        rows =
            query.SortDescending && query.SortBy is not null
                ? rows.OrderByDescending(r => EF.Property<object>(r, sort))
                : rows.OrderBy(r => EF.Property<object>(r, sort));
        var page = await rows.Skip(query.Page.Skip)
            .Take(query.Page.Size)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new AdminPage<T>(page, total);
    }

    /// <summary>The row is tracked, so an action given this context can change it and save.</summary>
    public virtual async Task<T?> FindAsync(object?[] key, CancellationToken ct) =>
        await Db.Set<T>().FindAsync(key, ct).ConfigureAwait(false);

    public virtual async Task<T> CreateAsync(T row, CancellationToken ct)
    {
        Db.Add(row);
        await Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <summary>Writes the changed properties onto the stored row; generated columns keep their stored values.</summary>
    public virtual async Task<T> UpdateAsync(
        T row,
        IReadOnlySet<string> changed,
        CancellationToken ct
    )
    {
        var key = Db
            .Model.FindEntityType(typeof(T))!
            .FindPrimaryKey()!
            .Properties.Select(p => p.PropertyInfo!.GetValue(row))
            .ToArray();
        var stored =
            await Db.Set<T>().FindAsync(key, ct).ConfigureAwait(false)
            ?? throw new AdminRejection("The row was deleted meanwhile.");
        var entry = Db.Entry(stored);
        foreach (var name in changed)
            entry.Property(name).CurrentValue = typeof(T).GetProperty(name)!.GetValue(row);
        await Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stored;
    }

    public virtual async Task DeleteAsync(T row, CancellationToken ct)
    {
        Db.Remove(row);
        await Db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static readonly MethodInfo EfLike = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like),
        [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)]
    )!;

    private static readonly MethodInfo ToLower = typeof(string).GetMethod(
        nameof(string.ToLower),
        Type.EmptyTypes
    )!;

    // lower(column) LIKE '%term%' with the term's own wildcards escaped; lower-casing both sides
    // is what makes it case-insensitive on Postgres, SQLite and SQL Server already are.
    private static Expression Like(Expression member, string term)
    {
        var escaped = term.ToLowerInvariant()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
        return Expression.Call(
            null,
            EfLike,
            Expression.Property(null, typeof(EF), nameof(EF.Functions)),
            Expression.Call(member, ToLower),
            Expression.Constant($"%{escaped}%"),
            Expression.Constant("\\")
        );
    }
}

public static class EntityFrameworkPanelExtensions
{
    /// <summary>
    /// Serves every table whose type is an entity of <typeparamref name="TDb"/> and that names no
    /// source with an <see cref="EfSource{TDb, T}"/>. The model supplies each entity's key, hides
    /// its navigations, and makes the properties the store generates read-only.
    /// </summary>
    public static PanelBuilder UseEntityFramework<TDb>(this PanelBuilder panel)
        where TDb : DbContext
    {
        panel.Shapes = (services, type) =>
        {
            using var scope = services.CreateScope();
            var entity = scope.ServiceProvider.GetRequiredService<TDb>().Model.FindEntityType(type);
            if (entity is null)
                return null;
            return new EntityShape(
                entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToList()
                    ?? throw new InvalidOperationException(
                        $"{type.Name} is keyless; a table needs a key."
                    ),
                entity
                    .GetProperties()
                    .Where(p => p.ValueGenerated != ValueGenerated.Never)
                    .Select(p => p.Name)
                    .ToHashSet(),
                entity
                    .GetNavigations()
                    .Cast<INavigationBase>()
                    .Concat(entity.GetSkipNavigations())
                    .Select(n => n.Name)
                    .ToHashSet()
            );
        };
        panel.DefaultSource = type =>
            (SourceModel)
                typeof(Sources)
                    .GetMethod(nameof(Sources.Class))!
                    .MakeGenericMethod(type, typeof(EfSource<,>).MakeGenericType(typeof(TDb), type))
                    .Invoke(null, [null])!;
        return panel;
    }
}
